import AppKit
import Foundation

public struct NativeTrashResult: @unchecked Sendable {
    public let relocated: [URL: URL]
    public let error: Error?
}

/// Holds file-coordination access until the asynchronous system recycle operation
/// finishes. All waits occur on a private queue, never the UI or Worker actors.
@MainActor public enum FileTrashCoordinator {
    public static func recycle(_ urls: [URL], authorize: @escaping @MainActor () async throws -> Void) async throws -> NativeTrashResult {
        try await withCheckedThrowingContinuation { continuation in
            let coordinator = NSFileCoordinator()
            let queue = OperationQueue()
            queue.name = "JavMetaLite.coordinatedTrash"; queue.maxConcurrentOperationCount = 1
            let intents = urls.map { NSFileAccessIntent.writingIntent(with: $0, options: .forDeleting) }
            coordinator.coordinate(with: intents, queue: queue) { coordinationError in
                if let coordinationError { continuation.resume(throwing: coordinationError); return }
                guard zip(intents, urls).allSatisfy({ $0.0.url.resolvingSymlinksInPath() == $0.1.resolvingSymlinksInPath() }) else {
                    continuation.resume(throwing: CocoaError(.fileWriteUnknown)); return
                }
                let finished = DispatchSemaphore(value: 0)
                Task { @MainActor in
                    do {
                        try await authorize()
                        NSWorkspace.shared.recycle(intents.map(\.url)) { relocated, error in
                            continuation.resume(returning: NativeTrashResult(relocated: relocated, error: error))
                            finished.signal()
                        }
                    } catch { continuation.resume(throwing: error); finished.signal() }
                }
                // No timeout may release an authorized lease while recycle is active.
                finished.wait()
            }
        }
    }
}
