import Foundation

// Every test owns a fresh preference domain. Production .standard preferences
// must neither influence tests nor be changed by persistent-control regressions.
final class TestPreferenceDomain {
    let name = "com.noredge.JavMetaLite.Tests." + UUID().uuidString
    let defaults: UserDefaults
    init() { defaults = UserDefaults(suiteName: name)!; defaults.removePersistentDomain(forName: name) }
    deinit { defaults.removePersistentDomain(forName: name) }
}
