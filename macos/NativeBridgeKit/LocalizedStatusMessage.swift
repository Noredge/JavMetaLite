import Foundation

/// Retains semantic status data so changing the UI language also updates an
/// already completed operation. Diagnostics are kept verbatim.
public struct LocalizedStatusMessage: Equatable {
    public let key: String
    public let counts: [Int]
    public let details: [String]

    public init(_ key: String, counts: [Int] = [], details: [String] = []) {
        self.key = key; self.counts = counts; self.details = details
    }

    public func resolved(using translate: (String) -> String) -> String {
        let format = translate(key)
        let message = counts.isEmpty ? format : String(format: format, arguments: counts.map { $0 as CVarArg })
        return ([message] + details).filter { !$0.isEmpty }.joined(separator: "\n")
    }
}
