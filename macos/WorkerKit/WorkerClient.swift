import Foundation
import Darwin

public actor WorkerClient {
    private var process: Process?
    private var input: FileHandle?
    private struct PendingRequest {
        let continuation: CheckedContinuation<JSONValue, Error>
        let deadline: ContinuousClock.Instant
        var data: Data?
        var offset = 0
        var timeout: Task<Void, Never>?
    }
    private var pending: [String: PendingRequest] = [:]
    private var writeQueue: [String] = []
    private var queuedBytes = 0
    private var writer: Task<Void, Never>?
    private let maximumPendingRequests = 128
    private let maximumQueuedBytes = 16 * 1024 * 1024
    // Testable accounting; payloads and deadline tasks leave together on every outcome.
    var requestRetention: (pending: Int, queued: Int, bytes: Int, timers: Int) {
        (pending.count, writeQueue.count, queuedBytes, pending.values.filter { $0.timeout != nil }.count)
    }
    private var nextID = 0
    private var framer = LineFramer()
    private var notification: (@Sendable (JSONValue) -> Void)?
    private var sessionGeneration = UUID()
    private var shuttingDown = false
    private var startInFlight = false
    private let requestTimeout: Duration
    public private(set) var stderrTail = ""
    public private(set) var handshake: JSONValue = .null

    public init(requestTimeout: Duration = .seconds(30)) { self.requestTimeout = requestTimeout }
    public func setNotificationHandler(_ handler: @escaping @Sendable (JSONValue) -> Void) { notification = handler }

    public static func bundledExecutable(bundle: Bundle = .main) throws -> URL {
        let bundleRoot = bundle.bundleURL.resolvingSymlinksInPath().standardizedFileURL
        let executable = bundleRoot.appendingPathComponent("Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker").resolvingSymlinksInPath().standardizedFileURL
        guard executable.path.hasPrefix(bundleRoot.path + "/"), FileManager.default.isExecutableFile(atPath: executable.path) else {
            throw RPCFailure(code: -32000, message: "The bundled engine is missing. Rebuild or reinstall the app.", data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
        }
        return executable
    }

    public func start(executable: URL, arguments: [String] = [], environment: [String: String]? = nil) async throws -> JSONValue {
        guard !startInFlight else {
            throw RPCFailure(code: -32010, message: "Engine initialization is already in progress.", data: .object(["reason": .string("ENGINE_START_IN_PROGRESS")]))
        }
        if process?.isRunning == true, input != nil, !shuttingDown, handshake["protocolVersion"].intValue == 1 { return handshake }
        startInFlight = true
        defer { startInFlight = false }
        if process?.isRunning != true {
            writer?.cancel(); writer = nil
            try? input?.close(); input = nil
            failPending(unavailable("The previous engine session ended. Unconfirmed operations must not be replayed."))
            shuttingDown = false
            handshake = .null
            framer = LineFramer()
            sessionGeneration = UUID()
            let generation = sessionGeneration
            let task = Process()
            let stdinPipe = Pipe(), stdoutPipe = Pipe(), stderrPipe = Pipe()
            // The engine can close stdin between isRunning and write. Keep the
            // resulting EPIPE throwable instead of terminating the Mac app.
            // Scope this to our descriptor; do not change process-wide signals.
            let descriptor = stdinPipe.fileHandleForWriting.fileDescriptor
            let flags = fcntl(descriptor, F_GETFL)
            guard flags != -1, fcntl(descriptor, F_SETFL, flags | O_NONBLOCK) != -1,
                  fcntl(descriptor, F_SETNOSIGPIPE, 1) != -1 else {
                throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
            }
            task.executableURL = executable
            task.arguments = arguments
            if let environment { task.environment = environment }
            task.standardInput = stdinPipe; task.standardOutput = stdoutPipe; task.standardError = stderrPipe
            task.terminationHandler = { [weak self] task in Task { await self?.didExit(status: task.terminationStatus, generation: generation) } }
            try task.run()
            self.process = task; self.input = stdinPipe.fileHandleForWriting
            // Independent drain queues: stderr saturation must never stop stdout responses.
            let stdoutStream = AsyncStream<Data>.makeStream()
            let stderrStream = AsyncStream<Data>.makeStream()
            Self.drain(stdoutPipe.fileHandleForReading, continuation: stdoutStream.continuation)
            Self.drain(stderrPipe.fileHandleForReading, continuation: stderrStream.continuation)
            Task { [weak self] in
                for await data in stdoutStream.stream { await self?.receive(data, generation: generation) }
            }
            Task { [weak self] in
                for await data in stderrStream.stream { await self?.receiveStderr(data, generation: generation) }
            }
        }
        guard input != nil, !shuttingDown else {
            throw RPCFailure(code: -32000, message: "The metadata engine connection is unavailable.", data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
        }
        // A timeout does not establish a handshake. Explicit startup retry may
        // resend this idempotent command to the same process; no movie operation
        // is replayed and a late response to the old request cannot authorize it.
        let generation = sessionGeneration
        let result = try await request("initialize", params: .object([
            "protocolVersion": .number(1), "clientVersion": .string("0.4.0-dev"),
            "locale": .string(Locale.preferredLanguages.first ?? "en")
        ]))
        guard generation == sessionGeneration, process?.isRunning == true, input != nil else {
            throw RPCFailure(code: -32000, message: "The metadata engine exited during initialization.", data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
        }
        guard result["protocolVersion"].intValue == 1 else {
            let error = RPCFailure(code: -32000, message: "Client and bundled engine versions do not match.", data: .object(["reason": .string("PROTOCOL_MISMATCH")]))
            closeTransport(error)
            throw error
        }
        handshake = result
        return result
    }

    private nonisolated static func drain(_ handle: FileHandle, continuation: AsyncStream<Data>.Continuation) {
        DispatchQueue(label: "JavMetaLite.pipe.\(UUID())", qos: .userInitiated).async {
            defer { continuation.finish(); try? handle.close() }
            while true {
                let data = handle.availableData
                guard !data.isEmpty else { break }
                continuation.yield(data)
            }
        }
    }

    public func request(_ method: String, params: JSONValue = .object([:])) async throws -> JSONValue {
        let deadline = ContinuousClock.now.advanced(by: requestTimeout)
        guard process?.isRunning == true, input != nil, !shuttingDown || method == "shutdown" else {
            throw unavailable("The metadata engine is unavailable.")
        }
        // Reject before retaining another encoded payload. Neither rejection nor
        // cancellation ever retries a request or authorizes replaying a save.
        guard pending.count < maximumPendingRequests else {
            throw RPCFailure(code: -32010, message: "Too many engine requests are pending.", data: .object(["reason": .string("CLIENT_BACKPRESSURE")]))
        }
        try Task.checkCancellation()
        nextID += 1
        let id = "swift-\(nextID)", generation = sessionGeneration
        let message = JSONValue.object(["jsonrpc": .string("2.0"), "id": .string(id), "method": .string(method), "params": params])
        var data = try JSONEncoder().encode(message)
        guard data.count <= LineFramer.maximumBytes else { throw RPCFailure(code: -32600, message: "Request exceeds 8 MiB.") }
        data.append(10)
        guard queuedBytes + data.count <= maximumQueuedBytes else {
            throw RPCFailure(code: -32010, message: "The engine request queue is full.", data: .object(["reason": .string("CLIENT_BACKPRESSURE")]))
        }
        guard ContinuousClock.now < deadline else { throw unavailable("The engine request timed out before transmission.") }
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                // Cancellation can precede registration. Checking here and
                // registering without suspension prevents an orphan continuation.
                guard !Task.isCancelled else { continuation.resume(throwing: CancellationError()); return }
                pending[id] = PendingRequest(continuation: continuation, deadline: deadline, data: data)
                writeQueue.append(id); queuedBytes += data.count
                pending[id]?.timeout = Task { [weak self] in
                    do { try await Task.sleep(until: deadline, clock: .continuous) }
                    catch { return }
                    await self?.expire(id, generation: generation, canceled: false)
                }
                if writer == nil {
                    writer = Task { [weak self] in await self?.writeRequests(generation: generation) }
                }
            }
        } onCancel: {
            Task { await self.expire(id, generation: generation, canceled: true) }
        }
    }

    private func unavailable(_ message: String) -> RPCFailure {
        RPCFailure(code: -32000, message: message, data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
    }
    private func takePending(_ id: String) -> PendingRequest? {
        guard let request = pending.removeValue(forKey: id) else { return nil }
        request.timeout?.cancel()
        if let data = request.data {
            queuedBytes -= data.count
            writeQueue.removeAll { $0 == id }
        }
        return request
    }
    private func expire(_ id: String, generation: UUID, canceled: Bool) {
        guard generation == sessionGeneration, let request = pending[id] else { return }
        let error = unavailable(canceled
            ? "The engine request was canceled. Its operation status is not yet confirmed."
            : "The engine request timed out. Its operation status is not yet confirmed.")
        if request.data != nil && request.offset > 0 {
            // A partial JSON frame cannot be followed by another request or sent
            // after its deadline. Close stdin; EOF is not an executable frame.
            // Process termination remains a separate, independently observed event.
            closeTransport(error)
        } else { takePending(id)?.continuation.resume(throwing: error) }
    }
    private func writeRequests(generation: UUID) async {
        defer { if generation == sessionGeneration { writer = nil } }
        while generation == sessionGeneration, !Task.isCancelled, let input, let id = writeQueue.first {
            guard var request = pending[id], let data = request.data else { writeQueue.removeFirst(); continue }
            guard ContinuousClock.now < request.deadline else { expire(id, generation: generation, canceled: false); continue }
            // O_NONBLOCK makes this syscall bounded even when the Worker is
            // alive but no longer draining stdin. Only this actor owns writes.
            let count = data.withUnsafeBytes { bytes in
                Darwin.write(input.fileDescriptor, bytes.baseAddress!.advanced(by: request.offset), min(64 * 1024, data.count - request.offset))
            }
            let writeError = errno
            if count > 0 {
                request.offset += count
                if request.offset == data.count {
                    request.data = nil
                    queuedBytes -= data.count; writeQueue.removeFirst()
                }
                pending[id] = request
                await Task.yield()
            } else if count < 0 && (writeError == EAGAIN || writeError == EWOULDBLOCK) {
                do { try await Task.sleep(for: .milliseconds(2)) } catch { return }
            } else if count < 0 && writeError == EINTR {
                await Task.yield()
            } else {
                closeTransport(unavailable("The engine input pipe could not be written (\(writeError)). Its operation status is not yet confirmed."))
                return
            }
        }
    }
    private func closeTransport(_ error: Error) {
        writer?.cancel()
        try? input?.close(); input = nil
        failPending(error)
        notification?(.object(["method": .string("worker.unavailable"), "params": .object(["message": .string(error.localizedDescription)])]))
    }

    private func receive(_ data: Data, generation: UUID) {
        guard generation == sessionGeneration else { return }
        do {
            for line in try framer.append(data) {
                let message = try JSONDecoder().decode(JSONValue.self, from: line)
                guard message["jsonrpc"].stringValue == "2.0" else { throw RPCFailure(code: -32600, message: "Invalid engine protocol response.") }
                let id = message["id"].stringValue
                if !id.isEmpty, let request = takePending(id) {
                    if message["error"] != .null {
                        let error = message["error"]
                        request.continuation.resume(throwing: RPCFailure(code: error["code"].intValue, message: error["message"].stringValue, data: error["data"]))
                    } else { request.continuation.resume(returning: message["result"]) }
                } else if !message["method"].stringValue.isEmpty { notification?(message) }
            }
        } catch {
            closeTransport(error)
        }
    }

    private func receiveStderr(_ data: Data, generation: UUID) {
        guard generation == sessionGeneration else { return }
        stderrTail += String(decoding: data, as: UTF8.self)
        if stderrTail.utf8.count > 32768 { stderrTail = String(stderrTail.suffix(16000)) }
    }
    private func didExit(status: Int32, generation: UUID) {
        guard generation == sessionGeneration else { return }
        writer?.cancel()
        try? input?.close(); input = nil
        process = nil; handshake = .null
        failPending(RPCFailure(code: -32000, message: "The metadata engine exited (\(status)). Save state must be checked before retrying.", data: .object(["reason": .string("WORKER_UNAVAILABLE")])))
        // Only Process termination establishes that no engine work can continue.
        // A request timeout or broken transport is deliberately a different event.
        if !shuttingDown { notification?(.object(["method": .string("worker.exited"), "params": .object(["exitCode": .number(Double(status))])])) }
    }
    private func failPending(_ error: Error) {
        let waiting = pending.values; pending.removeAll()
        writeQueue.removeAll(); queuedBytes = 0
        for request in waiting { request.timeout?.cancel(); request.continuation.resume(throwing: error) }
    }
    public func shutdown() async {
        shuttingDown = true
        if process?.isRunning == true { _ = try? await request("shutdown") }
        writer?.cancel()
        try? input?.close(); input = nil
        failPending(unavailable("The engine connection has closed. Any unconfirmed operation must be checked before retrying."))
    }
    public func isRunning() -> Bool { process?.isRunning == true }
}
