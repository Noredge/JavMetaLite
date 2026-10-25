import Foundation

public actor WorkerClient {
    private var process: Process?
    private var input: FileHandle?
    private var pending: [String: CheckedContinuation<JSONValue, Error>] = [:]
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
            shuttingDown = false
            handshake = .null
            framer = LineFramer()
            sessionGeneration = UUID()
            let generation = sessionGeneration
            let task = Process()
            let stdinPipe = Pipe(), stdoutPipe = Pipe(), stderrPipe = Pipe()
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
            try? input?.close(); input = nil
            throw RPCFailure(code: -32000, message: "Client and bundled engine versions do not match.", data: .object(["reason": .string("PROTOCOL_MISMATCH")]))
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
        guard process?.isRunning == true, let input else {
            throw RPCFailure(code: -32000, message: "The metadata engine is unavailable.", data: .object(["reason": .string("WORKER_UNAVAILABLE")]))
        }
        nextID += 1
        let id = "swift-\(nextID)"
        let message = JSONValue.object(["jsonrpc": .string("2.0"), "id": .string(id), "method": .string(method), "params": params])
        var data = try JSONEncoder().encode(message)
        guard data.count <= LineFramer.maximumBytes else { throw RPCFailure(code: -32600, message: "Request exceeds 8 MiB.") }
        data.append(10)
        return try await withCheckedThrowingContinuation { continuation in
            pending[id] = continuation
            do { try input.write(contentsOf: data) }
            catch { pending.removeValue(forKey: id)?.resume(throwing: error) }
            // Timeout never retries a save: the operation can still be running.
            Task { [weak self] in
                try? await Task.sleep(for: self?.requestTimeout ?? .seconds(30))
                await self?.timeout(id)
            }
        }
    }

    private func timeout(_ id: String) {
        pending.removeValue(forKey: id)?.resume(throwing: RPCFailure(code: -32000, message: "The engine response timed out. Its operation status is not yet confirmed.", data: .object(["reason": .string("WORKER_UNAVAILABLE")])))
    }

    private func receive(_ data: Data, generation: UUID) {
        guard generation == sessionGeneration else { return }
        do {
            for line in try framer.append(data) {
                let message = try JSONDecoder().decode(JSONValue.self, from: line)
                guard message["jsonrpc"].stringValue == "2.0" else { throw RPCFailure(code: -32600, message: "Invalid engine protocol response.") }
                let id = message["id"].stringValue
                if !id.isEmpty, let continuation = pending.removeValue(forKey: id) {
                    if message["error"] != .null {
                        let error = message["error"]
                        continuation.resume(throwing: RPCFailure(code: error["code"].intValue, message: error["message"].stringValue, data: error["data"]))
                    } else { continuation.resume(returning: message["result"]) }
                } else if !message["method"].stringValue.isEmpty { notification?(message) }
            }
        } catch {
            failPending(error)
            notification?(.object(["method": .string("worker.unavailable"), "params": .object(["message": .string(error.localizedDescription)])]))
            try? input?.close(); input = nil
        }
    }

    private func receiveStderr(_ data: Data, generation: UUID) {
        guard generation == sessionGeneration else { return }
        stderrTail += String(decoding: data, as: UTF8.self)
        if stderrTail.utf8.count > 32768 { stderrTail = String(stderrTail.suffix(16000)) }
    }
    private func didExit(status: Int32, generation: UUID) {
        guard generation == sessionGeneration else { return }
        input = nil; process = nil; handshake = .null
        failPending(RPCFailure(code: -32000, message: "The metadata engine exited (\(status)). Save state must be checked before retrying.", data: .object(["reason": .string("WORKER_UNAVAILABLE")])))
        if !shuttingDown { notification?(.object(["method": .string("worker.unavailable"), "params": .object(["exitCode": .number(Double(status))])])) }
    }
    private func failPending(_ error: Error) {
        let waiting = pending.values; pending.removeAll()
        for continuation in waiting { continuation.resume(throwing: error) }
    }
    public func shutdown() async {
        shuttingDown = true
        if process?.isRunning == true { _ = try? await request("shutdown") }
        try? input?.close(); input = nil
    }
    public func isRunning() -> Bool { process?.isRunning == true }
}
