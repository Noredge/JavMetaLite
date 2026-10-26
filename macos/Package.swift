// swift-tools-version: 6.0
import PackageDescription
let package = Package(
    name: "JavMetaLiteMac",
    defaultLocalization: "en",
    platforms: [.macOS(.v15)],
    products: [.executable(name: "JavMetaLite", targets: ["JavMetaLiteMac"]), .library(name: "WorkerKit", targets: ["WorkerKit"])],
    targets: [
        .target(name: "WorkerKit", path: "WorkerKit"),
        .target(name: "NativeBridgeKit", path: "NativeBridgeKit"),
        .executableTarget(name: "JavMetaLiteMac", dependencies: ["WorkerKit", "NativeBridgeKit"], path: "JavMetaLiteMac", exclude: ["Resources/Info.plist", "Resources/Localizable.xcstrings"], resources: [.copy("Resources/JavMetaLite.icns"), .process("Resources/en.lproj"), .process("Resources/zh-Hans.lproj"), .process("Resources/zh-Hant.lproj"), .process("Resources/ja.lproj")]),
        .testTarget(name: "WorkerKitTests", dependencies: ["WorkerKit"], path: "WorkerKitTests"),
        .testTarget(name: "NativeBridgeKitTests", dependencies: ["NativeBridgeKit", "WorkerKit", "JavMetaLiteMac"], path: "NativeBridgeKitTests")
    ],
    swiftLanguageModes: [.v5]
)
