#!/usr/bin/env python3
"""Record build inputs and verify a signed bundle/archive without requiring its SDK."""
import argparse
import base64
import ctypes
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import plistlib
import stat
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BUILD_MANIFEST = 'Contents/Resources/BUILD-MANIFEST.json'
SCHEMA_VERSION = 1


def require(condition, message):
    if not condition:
        raise ValueError(message)


def canonical_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + '\n').encode('utf-8')


def digest(path):
    hasher = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            hasher.update(block)
    return hasher.hexdigest()


def is_macho(path):
    with path.open('rb') as stream:
        return stream.read(4) in (b'\xfe\xed\xfa\xce', b'\xce\xfa\xed\xfe', b'\xfe\xed\xfa\xcf',
                                  b'\xcf\xfa\xed\xfe', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca',
                                  b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca')


def command(arguments, cwd=None):
    return subprocess.check_output(arguments, cwd=cwd, text=True, stderr=subprocess.PIPE).strip()


def safe_relative(value):
    path = PurePosixPath(value)
    require(bool(value) and not path.is_absolute() and '..' not in path.parts and str(path) == value,
            f'Unsafe manifest path: {value!r}')
    return path


def signature_attributes(path):
    if hasattr(os, 'listxattr'):
        return {name: hashlib.sha256(os.getxattr(path, name)).hexdigest()
                for name in os.listxattr(path) if name.startswith('com.apple.cs.')}
    # CPython exposes os.*xattr on Linux, but not on macOS. Darwin's native APIs
    # preserve generic .NET signatures without invoking a subprocess per attribute.
    require(sys.platform == 'darwin', 'Extended-attribute inventory requires macOS or os.listxattr')
    libc = ctypes.CDLL(None, use_errno=True)
    libc.listxattr.argtypes = [ctypes.c_char_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_int]
    libc.listxattr.restype = ctypes.c_ssize_t
    libc.getxattr.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint32, ctypes.c_int]
    libc.getxattr.restype = ctypes.c_ssize_t
    encoded = os.fsencode(path)
    length = libc.listxattr(encoded, None, 0, 0)
    require(0 <= length <= 4 * 1024 * 1024, f'Cannot read signature attributes: {path.name}')
    buffer = ctypes.create_string_buffer(length)
    require(libc.listxattr(encoded, buffer, length, 0) == length, f'Attributes changed during inventory: {path.name}')
    result = {}
    for name in buffer.raw.split(b'\0'):
        if not name.startswith(b'com.apple.cs.'):
            continue
        size = libc.getxattr(encoded, name, None, 0, 0, 0)
        require(0 <= size <= 4 * 1024 * 1024, f'Cannot read signature attribute: {name!r}')
        data = ctypes.create_string_buffer(size)
        require(libc.getxattr(encoded, name, data, size, 0, 0) == size, 'Signature attribute changed during inventory')
        result[name.decode()] = hashlib.sha256(data.raw).hexdigest()
    return result


def inventory(root):
    """Do not follow links; reject special files and links that escape the bundle."""
    root = root.resolve(strict=True)
    result = {}
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in sorted(directories + files):
            path = Path(directory) / name
            relative = path.relative_to(root).as_posix()
            info = path.lstat()
            if stat.S_ISLNK(info.st_mode):
                destination = path.resolve(strict=True)
                require(destination.is_relative_to(root), f'Bundle symlink escapes: {relative}')
                result[relative] = {'kind': 'symlink', 'target': os.readlink(path)}
            elif stat.S_ISREG(info.st_mode):
                result[relative] = {'kind': 'file', 'sha256': digest(path), 'bytes': info.st_size,
                                    'mode': stat.S_IMODE(info.st_mode), 'signatureAttributes': signature_attributes(path)}
            else:
                require(stat.S_ISDIR(info.st_mode), f'Unsupported bundle entry: {relative}')
    return result


def source_selected(relative):
    exact = {'global.json', 'NuGet.Config', 'LICENSE', 'THIRD_PARTY_NOTICES.md', '.gitignore',
             '.github/workflows/macos.yml', 'docs/macos/THIRD_PARTY_NOTICES.md', 'docs/macos/PACKAGE.md'}
    return relative in exact or relative.startswith(('JavMetaLite.', 'macos/', 'scripts/macos/',
                                                      'contracts/', 'fixtures/', 'docs/macos/licenses/'))


def export_paths(root):
    ignored = {'bin', 'obj', '.git', '.build', '.swiftpm', '__pycache__', 'artifacts', 'TestResults', 'xcuserdata'}
    return sorted(path.relative_to(root).as_posix() for path in root.rglob('*')
                  if path.is_file() and not ignored.intersection(path.relative_to(root).parts)
                  and path.name != '.DS_Store' and path.suffix != '.pyc')


def exported_source(root, allow_modified=False):
    path = root / 'SOURCE-MANIFEST.json'
    require(path.is_file(), 'No .git or SOURCE-MANIFEST.json: use a verified source export')
    manifest = json.loads(path.read_text())
    require(manifest.get('schemaVersion') == 1 and manifest.get('kind') == 'source-export', 'Unsupported source export manifest')
    changed = []
    for relative, expected in manifest['files'].items():
        safe_relative(relative)
        file = root / relative
        if not file.is_file() or file.is_symlink() or digest(file) != expected['sha256'] or stat.S_IMODE(file.stat().st_mode) != expected['mode']:
            changed.append(relative)
    paths = export_paths(root)
    changed += [value for value in paths if source_selected(value) and value not in manifest['files']]
    require(allow_modified or not changed, 'Source export changed: ' + ', '.join(changed[:8]) + '; use --allow-modified-source-export only for intentional edits')
    return paths, manifest, changed


def source_inputs(root=ROOT, allow_modified_export=False):
    if (root / '.git').exists():
        paths = command(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], root).split('\0')
        provenance = {'gitCommit': command(['git', 'rev-parse', 'HEAD'], root),
                      'dirtyWorkingTree': bool(command(['git', 'status', '--porcelain'], root)), 'origin': 'git-working-tree'}
    else:
        paths, exported, changed = exported_source(root, allow_modified_export)
        provenance = {'gitCommit': exported['source']['gitCommit'],
                      'dirtyWorkingTree': bool(changed) or exported['source']['dirtyWorkingTree'],
                      'origin': 'source-export', 'verifiedExport': not changed, 'intentionalExportChanges': changed,
                      'exportManifestSHA256': digest(root / 'SOURCE-MANIFEST.json')}
    files = {}
    for relative in sorted(set(paths)):
        if not relative:
            continue
        safe_relative(relative)
        path = root / relative
        if source_selected(relative) and path.is_file():
            require(not path.is_symlink(), f'Source input may not be a symlink: {relative}')
            files[relative] = {'sha256': digest(path), 'mode': stat.S_IMODE(path.stat().st_mode)}
    require(files, 'No source build inputs found')
    return {**provenance, 'inputDigest': hashlib.sha256(canonical_bytes(files)).hexdigest(), 'files': files}


def package_directory(assets, package_id, version):
    for folder in assets['packageFolders']:
        path = Path(folder) / package_id.lower() / version.lower()
        if path.is_dir():
            return path
    raise ValueError(f'Restored package unavailable: {package_id}/{version}')


def package_hashes(folder, package_id, version):
    stem = f'{package_id.lower()}.{version}'
    recorded = (folder / (stem + '.nupkg.sha512')).read_text().strip()
    hasher = hashlib.sha512()
    with (folder / (stem + '.nupkg')).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            hasher.update(block)
    require(base64.b64encode(hasher.digest()).decode() == recorded, f'NuGet archive hash mismatch: {package_id}/{version}')
    content = json.loads((folder / '.nupkg.metadata').read_text())['contentHash']
    # NuGet's normalized content hash and signed archive hash are distinct.
    return {'contentHashSHA512': content, 'archiveHashSHA512': recorded}


def dependencies(worker, arch, root=ROOT):
    assets = json.loads((root / 'JavMetaLite.Worker/obj/project.assets.json').read_text())
    lock_name = f'packages.osx-{arch}.lock.json'
    locks = {}
    for project in ('JavMetaLite.Core', 'JavMetaLite.Imaging.Skia', 'JavMetaLite.Worker'):
        path = root / project / lock_name
        require(path.is_file(), f'Missing dependency lock: {project}/{lock_name}')
        locks[f'{project}/{lock_name}'] = digest(path)
    packages = []
    for identity, value in sorted(assets['libraries'].items()):
        if value['type'] != 'package':
            continue
        package_id, version = identity.rsplit('/', 1)
        folder = package_directory(assets, package_id, version)
        hashes = package_hashes(folder, package_id, version)
        require(hashes['contentHashSHA512'] == value['sha512'], f'Restored package content hash mismatch: {identity}')
        packages.append({'id': package_id, 'version': version, **hashes})
    config = json.loads((worker / 'JavMetaLite.Worker.runtimeconfig.json').read_text())['runtimeOptions']
    runtime = next(item for item in config['includedFrameworks'] if item['name'] == 'Microsoft.NETCore.App')
    runtime_id = f'Microsoft.NETCore.App.Runtime.osx-{arch}'
    folder = package_directory(assets, runtime_id, runtime['version'])
    runtime_hashes = package_hashes(folder, runtime_id, runtime['version'])
    notices = json.loads((root / 'docs/macos/licenses/manifest.json').read_text())
    for entry in notices['files']:
        local = root / 'docs/macos/licenses' / entry['file']
        require(digest(local) == entry['sha256'], f'License text changed without review: {entry["file"]}')
        if entry.get('packageFile'):
            package_id = entry['package'].replace('{arch}', arch)
            upstream = package_directory(assets, package_id, entry['version']) / entry['packageFile']
            require(digest(upstream) == entry['sha256'], f'License differs from restored package: {entry["file"]}')
    return {'nugetLockFiles': locks, 'resolvedPackages': packages,
            'runtimePack': {'id': runtime_id, 'version': runtime['version'], **runtime_hashes},
            'publishedDepsSHA256': digest(worker / 'JavMetaLite.Worker.deps.json'),
            'licenseManifestSHA256': digest(root / 'docs/macos/licenses/manifest.json'),
            'note': 'Resolved packages can contain assets for other platforms; the payload inventory records what is actually distributed.'}


def build_manifest(app, dotnet, arch, configuration, sign_mode, root=ROOT, allow_modified_export=False):
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    try:
        xcode = command(['/usr/bin/xcodebuild', '-version'])
    except subprocess.CalledProcessError:
        xcode = 'Full Xcode unavailable; Apple Command Line Tools SwiftPM build'
    value = {'schemaVersion': SCHEMA_VERSION, 'kind': 'build-provenance',
             'application': {'version': info['CFBundleShortVersionString'], 'build': info['CFBundleVersion'],
                             'minimumMacOS': info['LSMinimumSystemVersion'], 'architecture': arch},
             'build': {'configuration': configuration, 'requestedSigningMode': sign_mode,
                       'rid': f'osx-{arch}', 'selfContained': True, 'trimmed': False, 'aot': False,
                       'singleFile': False, 'frontend': 'SwiftPM', 'nugetRestore': 'locked-mode'},
             'source': source_inputs(root, allow_modified_export),
             'toolchain': {'dotnetSDK': command([dotnet, '--version'], root),
                           'swift': command(['/usr/bin/xcrun', 'swift', '--version']),
                           'macOSSDK': command(['/usr/bin/xcrun', '--sdk', 'macosx', '--show-sdk-version']),
                           'macOSSDKBuild': command(['/usr/bin/xcrun', '--sdk', 'macosx', '--show-sdk-build-version']),
                           'hostOS': command(['/usr/bin/sw_vers', '-productVersion']),
                           'hostOSBuild': command(['/usr/bin/sw_vers', '-buildVersion']),
                           'xcode': xcode, 'python': sys.version.split()[0]},
             'dependencies': dependencies(app / 'Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS', arch, root),
             'localizations': {locale: {key: entry['localizations'][locale]['stringUnit']['value']
                                      for key, entry in json.loads((root / 'macos/JavMetaLiteMac/Resources/Localizable.xcstrings').read_text())['strings'].items()}
                               for locale in ('en', 'zh-Hans', 'zh-Hant', 'ja')},
             'reproducibility': 'Records exact source inputs, locked dependencies, toolchain and payload hashes. It does not promise byte-identical signed binaries or ZIPs across machines, signing identities or timestamps.'}
    expected_sdk = json.loads((root / 'global.json').read_text())['sdk']['version']
    require(value['toolchain']['dotnetSDK'] == expected_sdk, 'Build provenance SDK differs from global.json')
    return value


def payload_manifest(app, archive=None):
    build_path = app / BUILD_MANIFEST
    build = json.loads(build_path.read_text())
    require(build['schemaVersion'] == SCHEMA_VERSION and build['kind'] == 'build-provenance', 'Unsupported embedded build manifest')
    native = {}
    for path in sorted(app.rglob('*')):
        if path.is_file() and is_macho(path):
            native[path.relative_to(app).as_posix()] = {'architectures': command(['/usr/bin/lipo', '-archs', str(path)]).split()}
    result = {'schemaVersion': SCHEMA_VERSION, 'kind': 'signed-payload', 'build': build,
              'buildManifestSHA256': digest(build_path), 'bundleName': app.name, 'payload': inventory(app),
              'nativeCode': native}
    if archive:
        result['archive'] = {'name': archive.name, 'bytes': archive.stat().st_size, 'sha256': digest(archive)}
    return result


def verify_payload(app, manifest, archive=None):
    require(manifest.get('schemaVersion') == SCHEMA_VERSION and manifest.get('kind') == 'signed-payload',
            'Unsupported payload manifest')
    for path in manifest['payload']:
        safe_relative(path)
    actual = inventory(app)
    expected = manifest['payload']
    require(actual == expected, 'Bundle payload mismatch: ' + ', '.join(sorted(key for key in actual.keys() | expected.keys() if actual.get(key) != expected.get(key))[:12]))
    require(digest(app / BUILD_MANIFEST) == manifest['buildManifestSHA256'], 'Embedded provenance changed')
    require(json.loads((app / BUILD_MANIFEST).read_text()) == manifest['build'], 'Build provenance mismatch')
    if archive:
        recorded = manifest.get('archive', {})
        require(recorded.get('sha256') == digest(archive) and recorded.get('bytes') == archive.stat().st_size,
                'Archive content differs from payload manifest')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    build = commands.add_parser('build')
    build.add_argument('--app', type=Path, required=True)
    build.add_argument('--dotnet', required=True)
    build.add_argument('--arch', choices=['arm64', 'x64'], required=True)
    build.add_argument('--configuration', choices=['Debug', 'Release'], required=True)
    build.add_argument('--sign-mode', choices=['adhoc', 'developer-id'], required=True)
    build.add_argument('--output', type=Path)
    build.add_argument('--allow-modified-source-export', action='store_true')
    payload = commands.add_parser('payload')
    payload.add_argument('--app', type=Path, required=True)
    payload.add_argument('--output', type=Path, required=True)
    payload.add_argument('--archive', type=Path)
    verify = commands.add_parser('verify')
    verify.add_argument('--app', type=Path, required=True)
    verify.add_argument('--manifest', type=Path, required=True)
    verify.add_argument('--archive', type=Path)
    args = parser.parse_args()
    app = args.app.resolve(strict=True)
    if args.command == 'build':
        value = build_manifest(app, args.dotnet, args.arch, args.configuration, args.sign_mode, allow_modified_export=args.allow_modified_source_export)
        output = args.output or app / BUILD_MANIFEST
    elif args.command == 'payload':
        value = payload_manifest(app, args.archive)
        output = args.output
        require(not output.resolve().is_relative_to(app), 'External payload manifest must remain outside the signed app')
    else:
        verify_payload(app, json.loads(args.manifest.read_text()), args.archive)
        print('MANIFEST PASS: complete payload and embedded source/toolchain/dependency provenance')
        return
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(canonical_bytes(value))
    print(output)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        sys.exit(f'Manifest verification failed: {error}')
