#!/usr/bin/env python3
"""Preflight by default; explicit, resumable Apple notarization of a staged copy."""
import argparse
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid

HERE = Path(__file__).resolve().parent


def run(command):
    return subprocess.run([str(x) for x in command], check=True, capture_output=True, text=True, timeout=120)


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def json_command(command, runner=run):
    value = json.loads(runner(command).stdout)
    if not isinstance(value, dict):
        raise ValueError('Expected an object from the Apple tool.')
    return value


def signature_record(app, runner=run):
    response = runner(['/usr/bin/codesign', '-d', '--verbose=4', app])
    text = response.stdout + response.stderr
    def field(key):
        match = re.search(r'^' + key + r'=(.+)$', text, re.MULTILINE)
        if not match:
            raise ValueError('Missing signature field: ' + key)
        return match[1].strip()
    if 'Authority=Developer ID Application:' not in text or 'runtime' not in text:
        raise ValueError('A Developer ID Application signature with hardened runtime is required; ad-hoc cannot be notarized.')
    team = field('TeamIdentifier')
    if not re.fullmatch(r'[A-Z0-9]{10}', team):
        raise ValueError('Invalid Developer ID team.')
    return {'teamId': team, 'cdHash': field('CDHash')}


def preflight(app, team=None, runner=run):
    from sign_bundle import bundle_layout
    app, _, _, _ = bundle_layout(app)
    record = signature_record(app, runner)
    if team and record['teamId'] != team:
        raise ValueError('Unexpected Developer ID team.')
    runner([str(HERE / 'verify-package.sh'), app, '--expected-sign-mode', 'developer-id', '--expected-team-id', record['teamId']])
    for tool in ('notarytool', 'stapler'):
        runner(['/usr/bin/xcrun', '--find', tool])
    return app, record


def save_state(directory, state):
    temporary = directory / 'notary-state.json.tmp'
    with temporary.open('w') as output:
        json.dump(state, output, indent=2)
        output.write('\n')
        output.flush()
        os.fsync(output.fileno())
    temporary.replace(directory / 'notary-state.json')


@contextlib.contextmanager
def lock(directory):
    with (directory / '.notary.lock').open('a') as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise ValueError('Another process is already working on this notarization directory.') from error
        yield


def auth_arguments(state):
    profile = state.get('keychainProfile')
    if not isinstance(profile, str) or not profile.strip() or profile.startswith('-') or '\n' in profile:
        raise ValueError('A nonempty Keychain profile name is required; passwords/API keys are not accepted by this script.')
    result = ['--keychain-profile', profile]
    if state.get('keychain'):
        result += ['--keychain', state['keychain']]
    return result


def submission_id(value):
    if not isinstance(value, str):
        raise ValueError('Submission ID must be a UUID string.')
    return str(uuid.UUID(value))


def submit(directory, state, runner=run):
    if state['phase'] != 'prepared' or state.get('submissionId'):
        raise ValueError('Submission was already attempted. Resume or adopt the existing request; do not upload again.')
    state['phase'] = 'submitting'
    save_state(directory, state)
    try:
        reply = json_command(['/usr/bin/xcrun', 'notarytool', 'submit', directory / 'notary-input.zip', *auth_arguments(state), '--output-format', 'json'], runner)
        state['submissionId'] = submission_id(reply['id'])
        state['phase'] = 'submitted'
        save_state(directory, state)
        (directory / 'submission.json').write_text(json.dumps(reply, indent=2) + '\n')
    except Exception:
        state['phase'] = 'submission-unknown'
        save_state(directory, state)
        raise


def load_log(directory, state, runner=run):
    value = json_command(['/usr/bin/xcrun', 'notarytool', 'log', state['submissionId'], *auth_arguments(state)], runner)
    (directory / 'notary-log.json').write_text(json.dumps(value, indent=2) + '\n')
    if submission_id(value.get('jobId')) != state['submissionId']:
        raise ValueError('Notary log belongs to a different submission.')
    digest = value.get('sha256')
    if not isinstance(digest, str) or not re.fullmatch(r'[0-9a-fA-F]{64}', digest) or digest.lower() != state['uploadSha256']:
        raise ValueError('Notary log is missing or does not match the uploaded archive SHA-256.')
    return value


def poll(directory, state, runner=run):
    reply = json_command(['/usr/bin/xcrun', 'notarytool', 'info', state['submissionId'], *auth_arguments(state), '--output-format', 'json'], runner)
    if submission_id(reply['id']) != state['submissionId']:
        raise ValueError('Notary status belongs to a different submission.')
    (directory / 'notary-info.json').write_text(json.dumps(reply, indent=2) + '\n')
    status = reply.get('status')
    state['appleStatus'] = status
    if status == 'In Progress':
        state['phase'] = 'submitted'
        save_state(directory, state)
        return False
    if status != 'Accepted':
        state['phase'] = 'rejected' if status in ('Invalid', 'Rejected') else 'status-unknown'
        save_state(directory, state)
        if status in ('Invalid', 'Rejected'):
            load_log(directory, state, runner)
        raise ValueError(f'Notarization did not pass: {status!r}; see the saved Apple log.')
    log = load_log(directory, state, runner)
    issues = log.get('issues') or []
    if not isinstance(issues, list) or not all(isinstance(issue, dict) for issue in issues):
        raise ValueError('Notary log issues are malformed.')
    if any(issue.get('severity') == 'error' for issue in issues):
        raise ValueError('Apple reported an error even though status was Accepted; inspect the saved log.')
    state['notaryWarningCount'] = sum(issue.get('severity') == 'warning' for issue in issues)
    state['phase'] = 'accepted'
    save_state(directory, state)
    return True


def finish(directory, state, runner=run):
    if state.get('appleStatus') != 'Accepted' or state['phase'] not in ('accepted', 'stapling'):
        raise ValueError('Only an accepted Apple submission may be stapled and repackaged.')
    app = directory / 'staging/JavMetaLite.app'
    if signature_record(app, runner) != state['signature']:
        raise ValueError('Staged app signature changed since submission.')
    runner(['/usr/bin/codesign', '--verify', '--deep', '--strict', app])
    state['phase'] = 'stapling'
    save_state(directory, state)
    runner(['/usr/bin/xcrun', 'stapler', 'staple', app])
    runner(['/usr/bin/xcrun', 'stapler', 'validate', app])
    assessment = runner(['/usr/sbin/spctl', '--assess', '--type', 'execute', '--verbose=4', app])
    (directory / 'gatekeeper.txt').write_text(assessment.stdout + assessment.stderr)
    runner(['/usr/bin/codesign', '--verify', '--deep', '--strict', app])
    if signature_record(app, runner) != state['signature']:
        raise ValueError('App identity or code signature changed during stapling.')
    name = 'JavMetaLite-macOS-notarized.zip'
    # Validate the actual ZIP round trip, including generic .NET signature xattrs,
    # before exposing final filenames or marking the request complete.
    with tempfile.TemporaryDirectory(prefix='.notarized-package-', dir=directory) as temporary:
        candidate = Path(temporary) / name
        manifest = candidate.with_suffix('.zip.manifest.json')
        runner(['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', app, candidate])
        runner([sys.executable, HERE / 'release_manifest.py', 'payload', '--app', app, '--output', manifest, '--archive', candidate])
        verify_distribution(candidate, manifest, state, runner)
        digest = sha256(candidate)
        checksum = candidate.with_suffix('.zip.sha256')
        checksum.write_text(digest + '  ' + name + '\n')
        for source in (candidate, manifest, checksum): source.replace(directory / source.name)
    state.update(phase='complete', finalArchive=name, finalSha256=digest)
    save_state(directory, state)


def verify_distribution(archive, manifest, state, runner=run):
    with tempfile.TemporaryDirectory(prefix='JavMetaLite notarized 解包 ') as temporary:
        runner(['/usr/bin/ditto', '-x', '-k', archive, temporary])
        app = Path(temporary) / 'JavMetaLite.app'
        if signature_record(app, runner) != state['signature']:
            raise ValueError('Extracted distribution changed the submitted code identity.')
        runner([HERE / 'verify-package.sh', app, '--manifest', manifest, '--archive', archive,
                '--expected-sign-mode', 'developer-id', '--expected-team-id', state['signature']['teamId']])
        runner(['/usr/bin/xcrun', 'stapler', 'validate', app])
        runner(['/usr/sbin/spctl', '--assess', '--type', 'execute', '--verbose=4', app])


def adopt(directory, state, identifier, runner=run):
    if state['phase'] not in ('submitting', 'submission-unknown') or state.get('submissionId'):
        raise ValueError('Only an ambiguous upload can adopt a submission ID.')
    candidate = {**state, 'submissionId': submission_id(identifier)}
    load_log(directory, candidate, runner)  # Requires matching request ID and exact archive SHA.
    state.update(candidate, phase='submitted')
    save_state(directory, state)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path, nargs='?')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--keychain-profile')
    parser.add_argument('--keychain', type=Path)
    parser.add_argument('--team-id')
    parser.add_argument('--submit', action='store_true', help='Explicitly authorize uploading the staged signed app to Apple.')
    parser.add_argument('--resume', type=Path, metavar='OUTPUT_DIRECTORY', help='Query an existing request, then staple/repackage only if Accepted. Never uploads again.')
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--adopt-submission-id', help='Recover an ambiguous upload; requires the matching archive SHA in Apple log.')
    args = parser.parse_args(argv)
    if args.resume:
        if args.app or args.output or args.submit or args.keychain_profile or args.keychain or args.team_id:
            parser.error('--resume uses its saved non-secret configuration; do not combine with new submission options.')
        directory = args.resume.resolve(strict=True)
        with lock(directory):
            state = json.loads((directory / 'notary-state.json').read_text())
            if state.get('schemaVersion') != 1:
                raise ValueError('Unsupported notarization state schema.')
            auth_arguments(state)
            if sha256(directory / 'notary-input.zip') != state['uploadSha256']:
                raise ValueError('The original uploaded ZIP changed; refusing to resume.')
            app, record = preflight(directory / 'staging/JavMetaLite.app', state['signature']['teamId'])
            if record != state['signature']:
                raise ValueError('The staged signed app changed; refusing to resume.')
            if args.check_only:
                print(json.dumps({'phase': state['phase'], 'readOnly': True}, indent=2)); return
            if args.adopt_submission_id:
                adopt(directory, state, args.adopt_submission_id)
            if state['phase'] == 'complete':
                if sha256(directory / state['finalArchive']) != state['finalSha256']:
                    raise ValueError('Completed distribution ZIP changed.')
            elif state['phase'] in ('accepted', 'stapling'):
                finish(directory, state)
            elif state.get('submissionId'):
                if poll(directory, state): finish(directory, state)
            else:
                raise ValueError('Upload outcome unknown. Inspect Apple history and adopt its matching submission ID; this script will not upload twice.')
            print(json.dumps(state, indent=2)); return
    if not args.app or args.adopt_submission_id:
        parser.error('Provide a signed app, or --resume an existing directory.')
    app, record = preflight(args.app, args.team_id)
    if not args.submit or args.check_only:
        print(json.dumps({'phase': 'preflight-only', 'signature': record, 'uploaded': False, 'credentialsVerified': False}, indent=2)); return
    if not args.output:
        parser.error('--submit requires a new --output directory.')
    directory = args.output.resolve()
    if directory.is_relative_to(app):
        raise ValueError('Output must be outside the signed app.')
    state = {'schemaVersion': 1, 'phase': 'prepared', 'signature': record, 'keychainProfile': args.keychain_profile,
             'keychain': str(args.keychain.resolve()) if args.keychain else None}
    auth_arguments(state)
    directory.mkdir(parents=True, exist_ok=False)
    with lock(directory):
        staged = directory / 'staging/JavMetaLite.app'
        staged.parent.mkdir()
        run(['/usr/bin/ditto', app, staged])
        if signature_record(staged) != record:
            raise ValueError('Staged copy differs from the signed source.')
        run(['/usr/bin/codesign', '--verify', '--deep', '--strict', staged])
        upload = directory / 'notary-input.zip'
        run(['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', staged, upload])
        state['uploadSha256'] = sha256(upload)
        save_state(directory, state)
        submit(directory, state)
        if poll(directory, state): finish(directory, state)
        print(json.dumps(state, indent=2))
        if state['phase'] != 'complete':
            print('Submission is pending, not notarized delivery. Resume this directory to retrieve status and finish.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, json.JSONDecodeError, subprocess.SubprocessError) as error:
        message = (error.stderr or '').strip() if isinstance(error, subprocess.CalledProcessError) else str(error)
        print('NOTARIZATION STOPPED: ' + message, file=sys.stderr)
        sys.exit(1)
