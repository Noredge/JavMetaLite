import Foundation

public enum JSONValue: Codable, Sendable, Equatable {
    case string(String), number(Double), bool(Bool), object([String: JSONValue]), array([JSONValue]), null
    public init(from decoder: Decoder) throws {
        let value = try decoder.singleValueContainer()
        if value.decodeNil() { self = .null }
        else if let bool = try? value.decode(Bool.self) { self = .bool(bool) }
        else if let string = try? value.decode(String.self) { self = .string(string) }
        else if let number = try? value.decode(Double.self) { self = .number(number) }
        else if let object = try? value.decode([String: JSONValue].self) { self = .object(object) }
        else { self = .array(try value.decode([JSONValue].self)) }
    }
    public func encode(to encoder: Encoder) throws {
        var value = encoder.singleValueContainer()
        switch self {
        case .string(let item): try value.encode(item)
        case .number(let item): try value.encode(item)
        case .bool(let item): try value.encode(item)
        case .object(let item): try value.encode(item)
        case .array(let item): try value.encode(item)
        case .null: try value.encodeNil()
        }
    }
    public subscript(_ key: String) -> JSONValue { objectValue[key] ?? .null }
    public var stringValue: String { if case .string(let value) = self { return value }; return "" }
    public var intValue: Int { if case .number(let value) = self { return Int(value) }; return 0 }
    public var boolValue: Bool { if case .bool(let value) = self { return value }; return false }
    public var objectValue: [String: JSONValue] { if case .object(let value) = self { return value }; return [:] }
    public var arrayValue: [JSONValue] { if case .array(let value) = self { return value }; return [] }
    public static func from<T: Encodable>(_ item: T) throws -> JSONValue { try JSONDecoder().decode(JSONValue.self, from: JSONEncoder().encode(item)) }
}

/// Bounded pre-acknowledgement progress, never an operation result/receipt cache.
/// Actual outcomes are reconciled through operations.get / workspace.getSnapshot.
public struct OperationProgressCache {
    public let maximumCount: Int
    public let maximumBytes: Int
    private struct Entry { let sequence: Int; let progress: JSONValue; let bytes: Int; let access: Int }
    private var entries: [String: Entry] = [:]
    private var clock = 0
    public private(set) var retainedBytes = 0
    public var count: Int { entries.count }
    public init(maximumCount: Int = 128, maximumBytes: Int = 64 * 1024) {
        self.maximumCount = max(0, maximumCount); self.maximumBytes = max(0, maximumBytes)
    }
    public func progress(for id: String) -> JSONValue? { entries[id]?.progress }
    public mutating func remove(_ id: String) {
        if let entry = entries.removeValue(forKey: id) { retainedBytes -= entry.bytes }
    }
    public mutating func removeAll() { entries.removeAll(); retainedBytes = 0; clock = 0 }
    public mutating func receive(method: String, params: JSONValue, protecting activeID: String? = nil) -> JSONValue? {
        guard ["operation.progress", "operation.finished"].contains(method) else { return nil }
        let id = params["operationId"].stringValue, phase = params["phase"].stringValue
        guard !id.isEmpty, id.utf8.count <= 128, phase.utf8.count <= 128,
              case .number(let rawSequence) = params["sequence"], rawSequence.isFinite,
              rawSequence >= 0, rawSequence <= Double(Int32.max) else { return nil }
        let sequence = Int(rawSequence)
        guard sequence > (entries[id]?.sequence ?? -1) else { return nil }
        var details: [String: JSONValue] = [:]
        for key in ["currentIndex", "totalJobs"] {
            if case .number(let number) = params["details"][key], number.isFinite, number >= 0, number <= 10000 { details[key] = .number(number) }
        }
        let progress = JSONValue.object(["operationId": .string(id), "sequence": .number(Double(sequence)),
                                         "phase": .string(phase), "details": .object(details)])
        remove(id)
        // Discard finished notifications immediately, including potentially
        // multi-megabyte preview, query or import results and diagnostic text.
        if method == "operation.finished" { return progress }
        let bytes = (try? JSONEncoder().encode(progress).count) ?? Int.max
        guard maximumCount > 0, bytes <= maximumBytes else { return progress }
        clock += 1; entries[id] = Entry(sequence: sequence, progress: progress, bytes: bytes, access: clock); retainedBytes += bytes
        while count > maximumCount || retainedBytes > maximumBytes {
            guard let oldest = entries.filter({ $0.key != activeID }).min(by: { $0.value.access < $1.value.access }) else { break }
            remove(oldest.key)
        }
        return progress
    }
}

public struct RPCFailure: Error, LocalizedError, Sendable {
    public let code: Int
    public let message: String
    public let data: JSONValue
    public var reason: String { data["reason"].stringValue }
    public var errorDescription: String? { message }
    public init(code: Int, message: String, data: JSONValue = .null) { self.code = code; self.message = message; self.data = data }
}

/// Newline-delimited UTF-8 framing; arbitrary pipe boundaries never become message boundaries.
public struct LineFramer: Sendable {
    public static let maximumBytes = 8 * 1024 * 1024
    private var buffer = Data()
    private var scannedBytes = 0
    public init() {}
    public mutating func append(_ bytes: Data) throws -> [Data] {
        buffer.append(bytes)
        var lines: [Data] = []
        var lineStart = buffer.startIndex
        // The existing unfinished prefix contains no newline. Search only the
        // appended bytes, and compact completed frames once after scanning.
        var searchStart = buffer.index(buffer.startIndex, offsetBy: scannedBytes)
        while let newline = buffer[searchStart..<buffer.endIndex].firstIndex(of: 10) {
            let length = buffer.distance(from: lineStart, to: newline)
            guard length <= Self.maximumBytes else { throw RPCFailure(code: -32600, message: "Protocol message exceeds 8 MiB.") }
            let line = Data(buffer[lineStart..<newline])
            if !line.isEmpty { lines.append(line) }
            lineStart = buffer.index(after: newline)
            searchStart = lineStart
        }
        if lineStart != buffer.startIndex { buffer.removeSubrange(buffer.startIndex..<lineStart) }
        scannedBytes = buffer.count
        guard buffer.count <= Self.maximumBytes else { throw RPCFailure(code: -32600, message: "Protocol message exceeds 8 MiB.") }
        return lines
    }
    public var hasIncompleteMessage: Bool { !buffer.isEmpty }
}
