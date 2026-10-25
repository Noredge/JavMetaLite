#!/usr/bin/env python3
"""Sign this app from nested payload to outer bundle; never downgrade release signing."""
import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def run(command):
    return subprocess.run([str(x) for x in command], check=True, capture_output=True, text=True)


def signing_identity(mode, identity, expected_team, runner=run):
    if mode == 'adhoc':
        if identity or expected_team:
            raise ValueError('Ad-hoc signing cannot accept a Developer ID identity or team.')
        return '-', None
    if not identity:
        raise ValueError('Developer ID mode requires --identity; no ad-hoc fallback is allowed.')
    output = runner(['/usr/bin/security', 'find-identity', '-v', '-p', 'codesigning']).stdout
    candidates = re.findall(r'\b([0-9A-Fa-f]{40})\s+"(Developer ID Application: [^"\n]+)"', output)
    matching = [(sha, name) for sha, name in candidates if identity == name or identity.lower() == sha.lower()]
    if len(matching) != 1:
        raise ValueError('Select exactly one valid Developer ID Application identity by full name or SHA-1. None matched.')
    sha, name = matching[0]
    team_match = re.search(r'\(([A-Z0-9]{10})\)$', name)
    if not team_match:
        raise ValueError('Cannot determine the Developer ID team from the selected identity.')
    team = team_match[1]
    if expected_team and expected_team != team:
        raise ValueError('The signing identity belongs to a different team.')
    return sha, team


def bundle_layout(app):
    if app.is_symlink():
        raise ValueError('App itself must not be a symbolic link.')
    app = app.resolve(strict=True)
    if app.suffix != '.app' or not app.is_dir():
        raise ValueError('Expected an existing .app directory.')
    for child in app.rglob('*'):
        if child.is_symlink():
            raise ValueError(f'Unexpected symlink in the app: {child.relative_to(app)}')
    info = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != 'com.noredge.JavMetaLite':
        raise ValueError('This signer only supports the JavMetaLite bundle.')
    if info.get('CFBundleExecutable') != 'JavMetaLite':
        raise ValueError('Unexpected frontend executable name; paths are not allowed in CFBundleExecutable.')
    frontend = app / 'Contents/MacOS/JavMetaLite'
    helper = app / 'Contents/Helpers/JavMetaLiteWorker.bundle'
    helper_info = plistlib.loads((helper / 'Contents/Info.plist').read_bytes())
    if helper_info.get('CFBundleExecutable') != 'JavMetaLite.Worker':
        raise ValueError('Unexpected Worker executable name; paths are not allowed in CFBundleExecutable.')
    worker = helper / 'Contents/MacOS/JavMetaLite.Worker'
    if helper_info.get('CFBundleIdentifier') != info['CFBundleIdentifier'] + '.worker':
        raise ValueError('Unexpected Worker bundle identifier.')
    for executable, container in ((frontend, app / 'Contents/MacOS'), (worker, helper / 'Contents/MacOS')):
        if not executable.resolve(strict=True).is_relative_to(container.resolve(strict=True)):
            raise ValueError('Executable escapes its own MacOS directory.')
        if not executable.is_file() or not executable.stat().st_mode & 0o111:
            raise ValueError(f'Missing executable: {executable}')
    return app, frontend, helper, worker


def plan_signing(app, mode, identity=None, expected_team=None, runner=run):
    app, frontend, helper, worker = bundle_layout(app)
    selected, team = signing_identity(mode, identity, expected_team, runner)
    entitlements = HERE / ('worker-release.entitlements' if mode == 'developer-id' else 'worker-adhoc.entitlements')
    actual = plistlib.loads(entitlements.read_bytes())
    allowed = {'com.apple.security.cs.allow-jit': True}
    if mode == 'adhoc':
        allowed['com.apple.security.cs.disable-library-validation'] = True
    if actual != allowed:
        raise ValueError('Worker entitlements differ from the reviewed minimal signing policy.')
    timestamp = '--timestamp' if mode == 'developer-id' else '--timestamp=none'
    prefix = ['/usr/bin/codesign', '--force', '--sign', selected, timestamp]
    commands = []
    payload = helper / 'Contents/MacOS'
    # Generic signatures retain the existing .NET payload layout; native libraries
    # and executables receive hardened runtime. Managed/data files receive no JIT.
    for child in sorted(app.rglob('*')):
        if not child.is_file():
            continue
        kind = runner(['/usr/bin/file', '-b', child]).stdout
        if 'Mach-O' in kind and child != frontend and not child.is_relative_to(payload):
            raise ValueError(f'Unplanned native code outside the reviewed helper layout: {child.relative_to(app)}')
    for child in sorted(payload.rglob('*')):
        if not child.is_file() or child == worker:
            continue
        native = 'Mach-O' in runner(['/usr/bin/file', '-b', child]).stdout
        identifier = 'com.noredge.JavMetaLite.worker.payload.' + hashlib.sha256(str(child.relative_to(payload)).encode()).hexdigest()[:24]
        commands.append(prefix + ['--identifier', identifier] + (['--options', 'runtime'] if native else []) + [str(child)])
    commands.append(prefix + ['--options', 'runtime', '--entitlements', str(entitlements), '--identifier', 'com.noredge.JavMetaLite.worker', str(worker)])
    commands.append(prefix + ['--options', 'runtime', '--entitlements', str(entitlements), str(helper)])
    commands.append(prefix + ['--options', 'runtime', str(app)])
    return {'app': str(app), 'mode': mode, 'teamId': team, 'identity': selected, 'commands': [[str(x) for x in command] for command in commands]}


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('app', type=Path)
    p.add_argument('--mode', choices=['adhoc', 'developer-id'], required=True)
    p.add_argument('--identity')
    p.add_argument('--team-id')
    p.add_argument('--check-only', action='store_true', help='Validate and print the plan without modifying any signature.')
    p.add_argument('--report', type=Path)
    args = p.parse_args(argv)
    plan = plan_signing(args.app, args.mode, args.identity, args.team_id)
    if args.report and args.report.resolve().is_relative_to(Path(plan['app'])):
        raise ValueError('Signature report must be outside the signed bundle.')
    if args.check_only:
        print(json.dumps(plan, indent=2))
        return
    for command in plan['commands']:
        run(command)
    run(['/usr/bin/codesign', '--verify', '--deep', '--strict', '--verbose=2', plan['app']])
    plan['result'] = 'signed-and-verified'
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(plan, indent=2) + '\n')
    print(f"SIGN PASS: {args.mode}; {len(plan['commands'])} inside-out steps; team={plan['teamId'] or 'none'}")
    if args.mode == 'adhoc':
        print('Local ad-hoc build only; Developer ID, notarization and Gatekeeper acceptance are not established.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, plistlib.InvalidFileException, subprocess.CalledProcessError) as error:
        detail = error.stderr.strip() if isinstance(error, subprocess.CalledProcessError) else str(error)
        print('SIGN FAILED: ' + detail, file=sys.stderr)
        sys.exit(1)
