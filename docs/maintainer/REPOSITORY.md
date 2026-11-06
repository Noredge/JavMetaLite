# Repository layout

| Root | Ownership |
| --- | --- |
| `windows/` | WPF app, Windows imaging and Windows smoke/transaction/UI tests |
| `macos/` | Swift app/packages/Xcode project, JSON-RPC Worker and its regression host |
| `shared/` | Core, Skia imaging and portable tests |
| `contracts/` | Worker protocol schema and golden messages |
| `fixtures/` | Synthetic provider responses, media and demo generator |
| `scripts/` | Stable build/test/release entry points and optional maintenance tools |
| `packaging/` | Windows portable-package README template |
| `docs/` | Current guides and technical requirements; dated records in `archive/` |
| `.github/` | CI workflows and issue templates |

Root README translations, license/notices, changelog, `global.json` and `NuGet.Config` remain discoverable at the root. Build output stays ignored in `release/`, `artifacts/`, project `bin/obj/` and `macos/.build/`.

## Dependency directions

Windows App → shared Core + Windows imaging. macOS Swift App → bundled Worker → shared Core + Skia imaging. Portable tests reference shared projects and link `GatedDownloadHandler.cs` from the Windows regression project; this is synthetic test infrastructure, not a runtime frontend dependency.

Keep project names, assembly names, namespaces, package IDs and runtime bundle paths stable when moving source directories. Relative `ProjectReference` and linked `Compile` paths must resolve from each project directory. Dependency versions and RID lock files are independent of this layout change.

## Build and release contracts

Run scripts from the repository root using the [Windows guide](../../windows/README.md) or [macOS guide](../../macos/README.md). Existing `scripts/*.ps1` and `scripts/macos/*.sh` commands, their options, artifact names and output directories remain the public entry points. The CI workflow files, job names, triggers, signing modes and release permissions are unchanged by the directory move.

`scripts/ci/scope.py` contains exact source roots and pressure-sensitive paths. Update these together on any future move; additions, deletions, project/script/workflow changes and unknown paths still require full validation. `scripts/macos/release_manifest.py` must include Windows, shared and macOS source inputs and locate all three RID lock files. A source export must still rebuild without `.git`.

## Retention and removal

The repository organization pass moved all ten root .NET project directories and archived four release notes plus thirteen performance JSON files. No tracked implementation, script, fixture, license or historical measurement was deleted. Scripts were classified by their actual callers and manual purpose in the [inventory](../../scripts/README.md); absence from CI alone does not establish obsolescence.

Before removing a file, check tracked references, project includes, dynamically constructed paths, packaging manifests and documented manual entry points. Keep untracked local status files and private evidence outside commits. Current safety requirements, protocol specifications and release inputs belong in maintained documentation, even when their original filename mentions a regression or version.
