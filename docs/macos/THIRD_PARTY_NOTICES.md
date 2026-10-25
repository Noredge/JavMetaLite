# macOS bundle dependencies

Signing and notarization status are recorded separately for each package; the
dependency licenses below do not establish distribution approval. The macOS
payload contains these pinned components, in addition to the project's MIT code:

| Component | Version | Terms included |
| --- | --- | --- |
| AngleSharp | 1.5.2 | MIT, `licenses/AngleSharp-LICENSE.txt` |
| SkiaSharp | 3.119.0 | MIT, `licenses/SkiaSharp-LICENSE.txt` |
| SkiaSharp.NativeAssets.macOS | 3.119.0 | MIT and Skia/dependency notices, `licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt` |
| Microsoft.NETCore.App.Runtime.osx-arm64 / osx-x64 | 10.0.11 (SDK 10.0.400) | MIT, `licenses/DOTNET-LICENSE.txt`; full macOS runtime notices, `licenses/DOTNET-THIRD-PARTY-NOTICES.txt` |

The full SkiaSharp license/notices and .NET runtime license/notices were copied
from their exact restored macOS NuGet packages, without rewriting their terms.
The two .NET runtime architectures carry identical notice bytes for this version;
only the selected architecture's runtime is bundled. The AngleSharp license is
the unchanged upstream block already included in the repository notices.
`licenses/manifest.json` records each reviewed license's SHA-256 and package
origin. Building checks both the stored text and the actual restored upstream
files; package verification checks the embedded copies. Dependency upgrades
require reviewing and updating this manifest, not merely changing a version label.

RID-specific NuGet lock files pin the complete resolved package graph. This graph
also contains SkiaSharp.NativeAssets.Win32 as a transitive dependency; its Windows
binary assets are not copied into the macOS payload. BUILD-MANIFEST.json records
NuGet content and archive hashes separately, and the external payload manifest
records exactly which files were distributed.

The build copies all license files into `Contents/Resources/licenses` and includes
the original repository notices for provenance.
The Windows-only WebView2 and Windows Desktop components described in the repository
notices are not part of the macOS app. No source-site data is bundled as product data.
The demo fixtures are synthetic and use `example.invalid` for nonresolving URLs.

Intel packages are a separate build and verification target; an Apple Silicon result
does not establish Intel runtime or native dependency compatibility.
