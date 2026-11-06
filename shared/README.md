# Shared components

| Directory | Purpose and consumers |
| --- | --- |
| `JavMetaLite.Core/` | Metadata, providers, NFO, file organization and transaction safety; consumed by Windows and the macOS Worker |
| `JavMetaLite.Imaging.Skia/` | Portable image processor used by the macOS Worker and portable tests; the Windows app uses its WPF processor |
| `JavMetaLite.PortableTests/` | Portable Core/image regression executable; also links the synthetic download handler from the Windows regression project |

Run from the repository root with the SDK pinned in [global.json](../global.json):

```sh
dotnet run --project shared/JavMetaLite.PortableTests -c Release
```

The supported native-image test environment is macOS. [macOS full gate](../docs/macos/TESTING.md) runs this suite, Worker tests and Swift tests together. [Windows gate](../docs/windows/TESTING.md) exercises Core with the Windows image processor. The JSON-RPC Worker and its nonpublishable regression host live under [macos/](../macos/README.md); the shared Core does not depend on either frontend.
