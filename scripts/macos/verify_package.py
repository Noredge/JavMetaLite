#!/usr/bin/env python3
"""Check signed native payload, provenance and real bundled Worker without an SDK."""
import argparse
import json
import os
from pathlib import Path
import plistlib
import re
import subprocess
import sys
import tempfile
from release_manifest import BUILD_MANIFEST, digest, inventory, is_macho, require, verify_payload


def run(arguments, **kwargs):
    return subprocess.run([str(x) for x in arguments], check=True, text=True, capture_output=True, **kwargs)


def signature(path, mode, team, *, runtime=False, entitlements=None):
    run(['/usr/bin/codesign', '--verify', '--strict', path])
    detail = run(['/usr/bin/codesign', '-d', '--verbose=4', path]).stderr
    if mode == 'adhoc':
        require('Signature=adhoc' in detail, f'Expected ad-hoc signature: {path.name}')
    else:
        require('Authority=Developer ID Application:' in detail and 'Signature=adhoc' not in detail,
                f'Expected Developer ID signature: {path.name}')
        require(f'TeamIdentifier={team}' in detail, f'Different signing team: {path.name}')
        require('Timestamp=' in detail, f'Developer ID signature has no secure timestamp: {path.name}')
    if runtime:
        require(re.search(r'flags=.*\(.*runtime.*\)', detail), f'Hardened runtime missing: {path.name}')
    raw = run(['/usr/bin/codesign', '-d', '--entitlements', ':-', path]).stdout
    actual = plistlib.loads(raw.encode()) if raw.strip() else {}
    require(actual == (entitlements or {}), f'Unexpected entitlements: {path.name}: {sorted(actual)}')
    return detail


def load_commands(path, arch):
    text = run(['/usr/bin/otool', '-arch', arch, '-l', path]).stdout
    rpaths = re.findall(r'cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset', text)
    versions = re.findall(r'\bminos (\d+(?:\.\d+)*)', text)
    versions += re.findall(r'cmd LC_VERSION_MIN_MACOSX\s+cmdsize \d+\s+version (\d+(?:\.\d+)*)', text)
    return rpaths, versions


def version_tuple(value):
    return tuple((list(map(int, value.split('.'))) + [0, 0])[:3])


def declared_executable(bundle, info, expected):
    name = info.get('CFBundleExecutable')
    require(isinstance(name, str) and name == expected and Path(name).name == name,
            f'Unexpected bundle executable: {name!r}')
    directory = bundle / 'Contents/MacOS'
    path = directory / name
    require(not path.is_symlink() and path.is_file() and os.access(path, os.X_OK),
            f'Missing or indirect executable: {name}')
    require(path.resolve().is_relative_to(bundle.resolve()) and directory.resolve().is_relative_to(bundle.resolve()),
            f'Bundle executable escapes: {name}')
    return path


def native_dependencies(path, arch, app, executable, minimum):
    rpaths, versions = load_commands(path, arch)
    executable_rpaths, _ = load_commands(executable, arch)
    for version in versions:
        require(version_tuple(version) <= version_tuple(minimum),
                f'Native code requires macOS {version}, above declared {minimum}: {path.name}')
    identifiers = set()
    if path.suffix == '.dylib':
        identifiers = {line.strip() for line in run(['/usr/bin/otool', '-arch', arch, '-D', path]).stdout.splitlines()[1:]}
    dependencies = []
    for line in run(['/usr/bin/otool', '-arch', arch, '-L', path]).stdout.splitlines()[1:]:
        if not line.startswith('\t'):
            continue
        dependency = line.strip().split(' (')[0]
        if dependency in identifiers:
            continue  # LC_ID_DYLIB identifies this library; it is not a dependency.
        dependencies.append(dependency)
        if dependency.startswith(('/System/Library/', '/usr/lib/')):
            continue

        def expand(value):
            return value.replace('@loader_path', str(path.parent)).replace('@executable_path', str(executable.parent))

        if dependency.startswith('@rpath/'):
            candidates = [Path(expand(base)) / dependency[len('@rpath/'):] for base in rpaths + executable_rpaths]
        elif dependency.startswith(('@loader_path/', '@executable_path/')):
            candidates = [Path(expand(dependency))]
        else:
            candidates = []
        require(any(candidate.is_file() and candidate.resolve().is_relative_to(app) for candidate in candidates),
                f'Unresolved or external native dependency: {path.name}: {dependency}')
    return dependencies


def verify(args):
    require(not args.app.is_symlink(), 'App may not be a symlink')
    app = args.app.resolve(strict=True)
    frontend = app / 'Contents/MacOS/JavMetaLite'
    helper_bundle = app / 'Contents/Helpers/JavMetaLiteWorker.bundle'
    helper = helper_bundle / 'Contents/MacOS'
    worker = helper / 'JavMetaLite.Worker'
    require(app.suffix == '.app' and app.is_dir(), 'Expected an existing .app directory')
    inventory(app)  # Reject special files or escaping links before launching code.
    build = json.loads((app / BUILD_MANIFEST).read_text())
    require(build.get('schemaVersion') == 1 and build.get('kind') == 'build-provenance', 'Unsupported build manifest')
    requested_mode = build['build']['requestedSigningMode']
    mode = args.expected_sign_mode or requested_mode
    require(mode in ('adhoc', 'developer-id') and mode == requested_mode, 'Signing mode differs from embedded provenance')
    require(not (mode == 'adhoc' and args.expected_team_id), 'Ad-hoc packages have no signing team')
    if args.manifest:
        verify_payload(app, json.loads(args.manifest.read_text()), args.archive)
    else:
        require(not args.archive, '--archive requires --manifest')
    app_info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if icon_name := app_info.get('CFBundleIconFile'):
        require(Path(icon_name).name == icon_name and icon_name.endswith('.icns'), 'Unsafe app icon name')
        icon = app / 'Contents/Resources' / icon_name
        require(icon.is_file() and icon.read_bytes().startswith(b'icns'), 'Declared app icon is missing or invalid')
    helper_info = plistlib.loads((helper_bundle / 'Contents/Info.plist').read_bytes())
    require(app_info['CFBundleIdentifier'] == 'com.noredge.JavMetaLite', 'Unexpected app identifier')
    require(helper_info['CFBundleIdentifier'] == app_info['CFBundleIdentifier'] + '.worker', 'Unexpected helper identifier')
    declared_executable(app, app_info, 'JavMetaLite')
    declared_executable(helper_bundle, helper_info, 'JavMetaLite.Worker')
    for key in ('CFBundleVersion', 'CFBundleShortVersionString', 'LSMinimumSystemVersion'):
        require(app_info[key] == helper_info[key], f'App/helper mismatch: {key}')
    require(build['application']['version'] == app_info['CFBundleShortVersionString'] and
            build['application']['build'] == app_info['CFBundleVersion'], 'Version differs from build provenance')
    require(build['application']['minimumMacOS'] == app_info['LSMinimumSystemVersion'],
            'Minimum system version differs from build provenance')
    for path in (frontend, worker):
        require(path.is_file() and os.access(path, os.X_OK), f'Missing executable: {path.name}')
    for name in ('JavMetaLite.Worker.dll', 'JavMetaLite.Worker.deps.json', 'JavMetaLite.Worker.runtimeconfig.json',
                 'libhostfxr.dylib', 'libcoreclr.dylib', 'libSkiaSharp.dylib'):
        require((helper / name).is_file(), f'Self-contained dependency missing: {name}')
    config = json.loads((helper / 'JavMetaLite.Worker.runtimeconfig.json').read_text())['runtimeOptions']
    require('framework' not in config and 'frameworks' not in config and bool(config.get('includedFrameworks')),
            'Worker requires installed .NET')
    require(digest(helper / 'JavMetaLite.Worker.deps.json') == build['dependencies']['publishedDepsSHA256'],
            'Published dependencies differ from build provenance')
    runtime_version = next(item['version'] for item in config['includedFrameworks'] if item['name'] == 'Microsoft.NETCore.App')
    require(runtime_version == build['dependencies']['runtimePack']['version'], 'Runtime version differs from provenance')
    resources = app / 'Contents/Resources'
    license_manifest = resources / 'licenses/manifest.json'
    require(digest(license_manifest) == build['dependencies']['licenseManifestSHA256'], 'License manifest differs from provenance')
    for entry in json.loads(license_manifest.read_text())['files']:
        require(digest(resources / 'licenses' / entry['file']) == entry['sha256'], f'Missing or changed license: {entry["file"]}')
    for name in ('LICENSE.txt', 'MACOS_THIRD_PARTY_NOTICES.md', 'READ-ME.md'):
        require((resources / name).is_file(), f'Package documentation missing: {name}')
    # Historical packages verify against their signed provenance, not a newer checkout.
    localized_count = None
    for locale in ('en', 'zh-Hans', 'zh-Hant', 'ja'):
        require(locale in app_info['CFBundleLocalizations'], f'Undeclared language: {locale}')
        candidates = list(resources.rglob(f'{locale}.lproj/Localizable.strings'))
        require(len(candidates) == 1, f'Missing or ambiguous language: {locale}')
        actual = json.loads(run(['/usr/bin/plutil', '-convert', 'json', '-o', '-', candidates[0]]).stdout)
        require(actual == build['localizations'][locale], f'Bundled translations differ from provenance: {locale}')
        require(localized_count is None or len(actual) == localized_count, 'Language key counts differ')
        localized_count = len(actual)
    run(['/usr/bin/codesign', '--verify', '--deep', '--strict', app])
    outer_detail = run(['/usr/bin/codesign', '-d', '--verbose=4', app]).stderr
    found_team = re.search(r'^TeamIdentifier=(.+)$', outer_detail, re.M)
    team = args.expected_team_id or (found_team[1] if found_team else None)
    if mode == 'developer-id':
        require(team and re.fullmatch('[A-Z0-9]{10}', team), 'Cannot establish Developer ID team')
    signature(app, mode, team, runtime=True)
    signature(frontend, mode, team, runtime=True)
    worker_entitlements = {'com.apple.security.cs.allow-jit': True}
    if mode == 'adhoc':
        worker_entitlements['com.apple.security.cs.disable-library-validation'] = True
    signature(helper_bundle, mode, team, runtime=True, entitlements=worker_entitlements)
    expected_arch = {'arm64': 'arm64', 'x64': 'x86_64'}[build['application']['architecture']]
    frontend_arches = run(['/usr/bin/lipo', '-archs', frontend]).stdout.split()
    require(frontend_arches == [expected_arch], f'Wrong frontend architecture: {frontend_arches}')
    native_count = 0
    for path in sorted(app.rglob('*')):
        if not path.is_file():
            continue
        native = is_macho(path)
        if path.is_relative_to(helper):
            signature(path, mode, team, runtime=native, entitlements=worker_entitlements if path == worker else None)
        if not native:
            continue
        require(path == frontend or path.is_relative_to(helper), f'Unexpected native code: {path.relative_to(app)}')
        native_count += 1
        arches = run(['/usr/bin/lipo', '-archs', path]).stdout.split()
        require(expected_arch in arches, f'Architecture mismatch: {path.name}: {arches}')
        dependencies = native_dependencies(path, expected_arch, app, frontend if path == frontend else worker,
                                           app_info['LSMinimumSystemVersion'])
        if path == frontend:
            require(any('/WebKit.framework/' in value for value in dependencies), 'Native browser bridge is not linked')
            symbols = run(['/usr/bin/nm', frontend]).stdout
            for symbol in ('NativeTextSession', 'BrowserPageCapture', 'FileTrashCoordinator', 'ArtworkThumbnailCache'):
                require(symbol in symbols, f'Native bridge missing: {symbol}')
    with tempfile.TemporaryDirectory(prefix='JavMetaLite package 检查 ') as temp:
        env = {'HOME': temp, 'TMPDIR': temp, 'PATH': '/usr/bin:/bin', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1'}
        requests = [{'jsonrpc': '2.0', 'id': 'verify-init', 'method': 'initialize', 'params': {'protocolVersion': 1}},
                    {'jsonrpc': '2.0', 'id': 'verify-snapshot', 'method': 'workspace.getSnapshot', 'params': {}},
                    {'jsonrpc': '2.0', 'id': 'verify-shutdown', 'method': 'shutdown', 'params': {}}]
        result = run([worker, '--data-root', temp], input=''.join(json.dumps(x)+'\n' for x in requests), env=env, timeout=30)
        replies = [json.loads(line) for line in result.stdout.splitlines()]
        initialize = next(x for x in replies if x.get('id') == 'verify-init')['result']
        require(initialize['protocolVersion'] == 1 and initialize['workerVersion'].split('-')[0] ==
                app_info['CFBundleShortVersionString'], 'Bundled Worker handshake/version mismatch')
        for capability in ('writeNfo', 'writeArtwork', 'organizeVideo', 'batchSave', 'manualBrowserImport', 'localArtworkTrash'):
            require(initialize['capabilities'].get(capability), f'Missing capability: {capability}')
        snapshot = next(x for x in replies if x.get('id') == 'verify-snapshot')['result']
        require(snapshot['jobs'] == [] and snapshot['activeOperations'] == [], 'Isolated Worker snapshot is not empty')
        require(any(x.get('id') == 'verify-shutdown' and 'result' in x for x in replies), 'No shutdown acknowledgement')
        require(all(x.get('jsonrpc') == '2.0' for x in replies), 'Nonprotocol stdout')
    print(f'PACKAGE PASS: {expected_arch}, {mode}, {native_count} native files, {localized_count} keys × 4 languages, complete signatures/entitlements/dependencies, no-SDK Worker initialize/snapshot/shutdown')
    print('Notarization and Gatekeeper acceptance require separate recorded checks.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--expected-sign-mode', choices=['adhoc', 'developer-id'])
    parser.add_argument('--expected-team-id')
    args = parser.parse_args()
    try:
        verify(args)
    except (ValueError, OSError, KeyError, StopIteration, subprocess.SubprocessError) as error:
        detail = error.stderr.strip() if isinstance(error, subprocess.CalledProcessError) else str(error)
        sys.exit('PACKAGE FAILED: ' + detail)


if __name__ == '__main__':
    main()
