#!/usr/bin/env python3
"""Check Xcode membership and typecheck its flat modules with Apple developer tools.

This is a CLT check, not a claim that xcodebuild, signing or hosted UI tests ran.
"""
import argparse
import json
from pathlib import Path
import platform
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
MAC = ROOT / 'macos'


def source_paths(objects, target_name):
    target = next(value for value in objects.values()
                  if value.get('isa') == 'PBXNativeTarget' and value['name'] == target_name)
    result = []
    for phase_id in target['buildPhases']:
        phase = objects[phase_id]
        if phase['isa'] == 'PBXSourcesBuildPhase':
            result += [objects[objects[build]['fileRef']]['path'] for build in phase['files']]
    assert len(result) == len(set(result)), f'Duplicate sources in {target_name}'
    return set(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=MAC / 'JavMetaLiteMac.xcodeproj/project.pbxproj')
    parser.add_argument('--arch', choices=['arm64', 'x86_64'], default=platform.machine())
    parser.add_argument('--check-only', action='store_true')
    args = parser.parse_args()
    project = json.loads(subprocess.check_output(['/usr/bin/plutil', '-convert', 'json', '-o', '-', str(args.project)]))
    objects = project['objects']
    production = {str(path.relative_to(MAC)) for folder in ['JavMetaLiteMac', 'NativeBridgeKit', 'WorkerKit']
                  for path in (MAC / folder).rglob('*.swift')}
    tests = {str(path.relative_to(MAC)) for folder in ['NativeBridgeKitTests', 'WorkerKitTests']
             for path in (MAC / folder).rglob('*.swift')}
    for name, expected in [('JavMetaLiteMac', production), ('JavMetaLiteMacTests', tests)]:
        actual = source_paths(objects, name)
        assert actual == expected, (f'{name} source membership drift; '
                                    f'missing={sorted(expected - actual)}, extra={sorted(actual - expected)}')
        assert all((MAC / path).is_file() for path in actual)
    print(f'XCODE SOURCE MEMBERSHIP PASS: {len(production)} app, {len(tests)} test sources', flush=True)
    if args.check_only:
        return
    swiftc = Path(subprocess.check_output(['/usr/bin/xcrun', '--find', 'swiftc'], text=True).strip())
    sdk = subprocess.check_output(['/usr/bin/xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip()
    flags = [str(swiftc), '-sdk', sdk, '-target', f'{args.arch}-apple-macos15.0', '-swift-version', '5']
    plugin = swiftc.parent.parent / 'lib/swift/host/plugins/testing/libTestingMacros.dylib'
    test_flags = ['-load-plugin-library', str(plugin)] if plugin.is_file() else []
    developer = Path(subprocess.check_output(['/usr/bin/xcode-select', '-p'], text=True).strip())
    # SwiftPM/Xcode add their testing-framework search paths automatically;
    # standalone swiftc needs the installed CLT/Xcode framework directory.
    for folder in [developer / 'Library/Developer/Frameworks',
                   developer / 'Platforms/MacOSX.platform/Developer/Library/Frameworks']:
        if (folder / 'Testing.framework').is_dir():
            test_flags += ['-F', str(folder)]
    with tempfile.TemporaryDirectory(prefix='JavMetaLite Xcode module check ') as temporary:
        module = Path(temporary) / 'JavMetaLiteMac.swiftmodule'
        # No SWIFT_PACKAGE define: compile the exact import/module layout used by Xcode.
        subprocess.run([*flags, '-parse-as-library', '-enable-testing', '-module-name', 'JavMetaLiteMac',
                        '-emit-module', '-emit-module-path', str(module),
                        *[str(MAC / path) for path in sorted(production)]], check=True)
        subprocess.run([*flags, *test_flags, '-I', temporary, '-module-name', 'JavMetaLiteMacTests',
                        '-typecheck', *[str(MAC / path) for path in sorted(tests)]], check=True)
    print('XCODE MODULE TYPECHECK PASS: production and tests; full Xcode build/UI not executed', flush=True)


if __name__ == '__main__':
    main()
