#!/usr/bin/env python3
"""Release command contracts. Apple service/Developer ID responses are fixtures, not acceptance."""
import json
import contextlib
import importlib.util
import io
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
import uuid

import notarize_release as notary
import sign_bundle as signer

_readiness_spec = importlib.util.spec_from_file_location('release_readiness', Path(__file__).with_name('release-readiness.py'))
readiness = importlib.util.module_from_spec(_readiness_spec)
_readiness_spec.loader.exec_module(readiness)

TEAM = 'ABCDE12345'
IDENTITY = 'Developer ID Application: Test Fixture (' + TEAM + ')'
DIGEST = 'A' * 40
REQUEST = str(uuid.UUID('12345678-1234-1234-1234-123456789abc'))


class SignTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='JavMetaLite release contract 中文 ')
        self.addCleanup(self.tmp.cleanup)
        self.app = Path(self.tmp.name) / 'JavMetaLite.app'
        self.helper = self.app / 'Contents/Helpers/JavMetaLiteWorker.bundle'
        for base, identifier, executable in [(self.app, 'com.noredge.JavMetaLite', 'JavMetaLite'), (self.helper, 'com.noredge.JavMetaLite.worker', 'JavMetaLite.Worker')]:
            (base / 'Contents/MacOS').mkdir(parents=True)
            (base / 'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier': identifier, 'CFBundleExecutable': executable}))
            binary = base / 'Contents/MacOS' / executable
            binary.write_bytes(b'native fixture'); binary.chmod(0o755)
        (self.helper / 'Contents/MacOS/data.json').write_text('{}')
        (self.helper / 'Contents/MacOS/library.dylib').write_bytes(b'native fixture')

    def runner(self, command):
        args = [str(x) for x in command]
        if args[0].endswith('security'):
            out = f'1) {DIGEST} "{IDENTITY}"\n 1 valid identities found'
        elif args[0].endswith('file'):
            out = 'JSON text' if args[-1].endswith(('.json', '.plist')) else 'Mach-O 64-bit'
        else:
            raise AssertionError(args)
        return subprocess.CompletedProcess(args, 0, out, '')

    def test_adhoc_signs_inside_out_without_deep_or_jit_on_swift(self):
        plan = signer.plan_signing(self.app, 'adhoc', runner=self.runner)
        self.assertEqual(plan['commands'][-1][-1], str(self.app.resolve()))
        self.assertEqual(plan['commands'][-2][-1], str(self.helper.resolve()))
        self.assertNotIn('--entitlements', plan['commands'][-1])
        self.assertTrue(all('--deep' not in command for command in plan['commands']))
        self.assertTrue(all('--timestamp=none' in command for command in plan['commands']))

    def test_release_selects_only_developer_id_and_uses_secure_timestamp(self):
        plan = signer.plan_signing(self.app, 'developer-id', IDENTITY, TEAM, self.runner)
        self.assertEqual(plan['teamId'], TEAM)
        self.assertTrue(all('--timestamp' in command and '--timestamp=none' not in command for command in plan['commands']))
        entitlement = Path(plan['commands'][-2][plan['commands'][-2].index('--entitlements') + 1])
        self.assertEqual(plistlib.loads(entitlement.read_bytes()), {'com.apple.security.cs.allow-jit': True})

    def test_missing_release_identity_never_downgrades(self):
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'developer-id', runner=self.runner)

    def test_wrong_team_fails(self):
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'developer-id', IDENTITY, 'WRONG12345', self.runner)

    def test_adhoc_rejects_identity(self):
        with self.assertRaises(ValueError): signer.signing_identity('adhoc', IDENTITY, None, self.runner)

    def test_ambiguous_identity_fails(self):
        def duplicate(command):
            value = self.runner(command); value.stdout += '\n' + value.stdout; return value
        with self.assertRaises(ValueError): signer.signing_identity('developer-id', IDENTITY, TEAM, duplicate)

    def test_unknown_identity_fails(self):
        with self.assertRaises(ValueError): signer.signing_identity('developer-id', 'Not a selected identity', None, self.runner)

    def test_symlink_is_refused_before_signing(self):
        (self.app / 'Contents/escape').symlink_to('/tmp')
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'adhoc', runner=self.runner)

    def test_frontend_executable_cannot_traverse_outside_macos(self):
        path = self.app / 'Contents/Info.plist'
        info = plistlib.loads(path.read_bytes()); info['CFBundleExecutable'] = '../../external'
        path.write_bytes(plistlib.dumps(info))
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'adhoc', runner=self.runner)

    def test_helper_executable_cannot_be_an_absolute_external_path(self):
        outside = Path(self.tmp.name) / 'external'; outside.write_bytes(b'protected'); outside.chmod(0o755)
        path = self.helper / 'Contents/Info.plist'
        info = plistlib.loads(path.read_bytes()); info['CFBundleExecutable'] = str(outside)
        path.write_bytes(plistlib.dumps(info))
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'adhoc', runner=self.runner)
        self.assertEqual(outside.read_bytes(), b'protected')

    def test_unplanned_native_code_fails(self):
        extra = self.app / 'Contents/Resources/extra.dylib'; extra.parent.mkdir(); extra.write_bytes(b'native')
        with self.assertRaises(ValueError): signer.plan_signing(self.app, 'adhoc', runner=self.runner)


class NotaryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='JavMetaLite notary contract 中文 ')
        self.addCleanup(self.tmp.cleanup)
        self.directory = Path(self.tmp.name)
        self.state = {'schemaVersion': 1, 'phase': 'submitted', 'submissionId': REQUEST, 'keychainProfile': 'Fixture Profile', 'uploadSha256': 'b' * 64}
        self.commands = []

    def runner(self, status='In Progress', request=REQUEST, digest='b' * 64, issues=None):
        def call(command):
            command = [str(x) for x in command]; self.commands.append(command)
            if 'info' in command: out = {'id': request, 'status': status}
            elif 'log' in command: out = {'jobId': request, 'sha256': digest, 'issues': issues}
            elif 'submit' in command: out = {'id': REQUEST, 'message': 'Fixture only'}
            else: raise AssertionError(command)
            return subprocess.CompletedProcess(command, 0, json.dumps(out), '')
        return call

    def test_pending_is_not_accepted_and_never_staples(self):
        self.assertFalse(notary.poll(self.directory, self.state, self.runner()))
        self.assertEqual(self.state['phase'], 'submitted')
        self.assertFalse(any('staple' in x for x in self.commands))

    def test_rejected_saves_log_and_does_not_finalize(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.runner('Invalid'))
        self.assertEqual(self.state['phase'], 'rejected')
        self.assertTrue((self.directory / 'notary-log.json').exists())

    def test_unknown_status_does_not_finalize(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.runner('Unexpected'))
        self.assertEqual(self.state['phase'], 'status-unknown')

    def test_accepted_records_warnings_and_exact_upload_hash(self):
        self.assertTrue(notary.poll(self.directory, self.state, self.runner('Accepted', issues=[{'severity': 'warning', 'message': 'Fixture'}])))
        self.assertEqual(self.state['notaryWarningCount'], 1)
        self.assertEqual(self.state['phase'], 'accepted')

    def test_mismatched_status_id_is_rejected(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.runner('Accepted', str(uuid.uuid4())))

    def test_mismatched_archive_log_is_rejected(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.runner('Accepted', digest='c' * 64))
        self.assertNotEqual(self.state['phase'], 'accepted')

    def test_error_issue_even_with_accepted_status_blocks_finalization(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.runner('Accepted', issues=[{'severity': 'error'}]))
        self.assertNotEqual(self.state['phase'], 'accepted')

    def test_uncertain_submit_is_not_retried(self):
        state = {**self.state, 'phase': 'prepared'}; state.pop('submissionId')
        def timeout(command): raise subprocess.TimeoutExpired(command, 120)
        with self.assertRaises(subprocess.TimeoutExpired): notary.submit(self.directory, state, timeout)
        self.assertEqual(state['phase'], 'submission-unknown')
        with self.assertRaises(ValueError): notary.submit(self.directory, state, self.runner())
        self.assertEqual(json.loads((self.directory / 'notary-state.json').read_text())['phase'], 'submission-unknown')

    def test_successful_submit_records_id_before_polling_and_never_uploads_twice(self):
        state = {**self.state, 'phase': 'prepared'}; state.pop('submissionId')
        notary.submit(self.directory, state, self.runner())
        self.assertEqual(state['submissionId'], REQUEST)
        with self.assertRaises(ValueError): notary.submit(self.directory, state, self.runner())
        self.assertEqual(len(self.commands), 1)

    def test_staple_before_accepted_is_forbidden(self):
        with self.assertRaises(ValueError): notary.finish(self.directory, self.state, self.runner())
        self.assertEqual(self.commands, [])

    def failing_finalizer(self, fail_step):
        def call(command):
            command = [str(x) for x in command]; self.commands.append(command)
            if '-d' in command:
                return subprocess.CompletedProcess(command, 0, '', 'Authority=Developer ID Application: Fixture\nTeamIdentifier=' + TEAM + '\nCDHash=' + 'd' * 40 + '\nflags=0x10000(runtime)\n')
            if fail_step in command:
                raise subprocess.CalledProcessError(1, command, '', 'fixture refusal')
            return subprocess.CompletedProcess(command, 0, '', '')
        return call

    def accepted_state(self):
        return {**self.state, 'phase': 'accepted', 'appleStatus': 'Accepted', 'signature': {'teamId': TEAM, 'cdHash': 'd' * 40}}

    def test_staple_failure_never_creates_distribution(self):
        state = self.accepted_state()
        with self.assertRaises(subprocess.CalledProcessError): notary.finish(self.directory, state, self.failing_finalizer('staple'))
        self.assertNotEqual(state['phase'], 'complete')
        self.assertFalse((self.directory / 'JavMetaLite-macOS-notarized.zip').exists())
        self.assertFalse(any('--assess' in x for x in self.commands))

    def test_gatekeeper_refusal_never_creates_distribution(self):
        state = self.accepted_state()
        with self.assertRaises(subprocess.CalledProcessError): notary.finish(self.directory, state, self.failing_finalizer('--assess'))
        self.assertNotEqual(state['phase'], 'complete')
        self.assertFalse((self.directory / 'JavMetaLite-macOS-notarized.zip').exists())
        self.assertTrue(any('validate' in x for x in self.commands))

    def missing_log_field(self, field):
        def call(command):
            command = [str(x) for x in command]; self.commands.append(command)
            value = {'id': REQUEST, 'status': 'Accepted'} if 'info' in command else {'jobId': REQUEST, 'sha256': 'b' * 64}
            if 'log' in command: value.pop(field)
            return subprocess.CompletedProcess(command, 0, json.dumps(value), '')
        return call

    def test_accepted_log_requires_job_id(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.missing_log_field('jobId'))
        self.assertNotEqual(self.state['phase'], 'accepted')

    def test_accepted_log_requires_archive_hash(self):
        with self.assertRaises(ValueError): notary.poll(self.directory, self.state, self.missing_log_field('sha256'))
        self.assertNotEqual(self.state['phase'], 'accepted')

    def test_killed_submitting_without_id_can_adopt_matching_apple_request(self):
        state = {**self.state, 'phase': 'submitting'}; state.pop('submissionId')
        notary.save_state(self.directory, state)
        notary.adopt(self.directory, state, REQUEST, self.runner('Accepted'))
        self.assertEqual(state['phase'], 'submitted')
        self.assertEqual(state['submissionId'], REQUEST)
        self.assertFalse(any('submit' in command for command in self.commands))

    def test_final_zip_is_extracted_and_checked_with_manifest_archive_and_ticket(self):
        state = self.accepted_state()
        archive = self.directory / 'candidate.zip'; manifest = self.directory / 'candidate.zip.manifest.json'
        notary.verify_distribution(archive, manifest, state, self.failing_finalizer('__no_failure__'))
        verify = next(command for command in self.commands if command[0].endswith('verify-package.sh'))
        self.assertEqual(verify[verify.index('--manifest') + 1], str(manifest))
        self.assertEqual(verify[verify.index('--archive') + 1], str(archive))
        self.assertTrue(any('ditto' in command[0] and '-x' in command for command in self.commands))
        self.assertTrue(any('validate' in command for command in self.commands))
        self.assertTrue(any('--assess' in command for command in self.commands))

    def test_empty_keychain_profile_is_forbidden(self):
        with self.assertRaises(ValueError): notary.auth_arguments({'keychainProfile': ''})

    def test_bad_submission_id_is_not_a_command_argument(self):
        with self.assertRaises(ValueError): notary.submission_id('--something')

    def test_lock_prevents_parallel_finalization(self):
        with notary.lock(self.directory):
            with self.assertRaises(ValueError):
                with notary.lock(self.directory): pass


class ReadinessTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='JavMetaLite readiness contract ')
        self.addCleanup(self.tmp.cleanup)
        self.app = Path(self.tmp.name) / 'Fixture.app'; self.app.mkdir()
        self.commands = []

    def checker(self, mode='developer-id', package=0, ticket=1, gatekeeper=1, signature=0):
        def call(command):
            command = [str(x) for x in command]; self.commands.append(command)
            code, out, error = 0, '', ''
            if command[0].endswith('codesign'):
                code = signature
                error = 'Authority=Developer ID Application: Fixture' if mode == 'developer-id' else ('Signature=adhoc' if mode == 'adhoc' else 'unsigned')
            elif command[0].endswith('security'): out = '0 valid identities found'
            elif command[0].endswith('verify-package.sh'): code = package
            elif 'validate' in command: code = ticket
            elif '--assess' in command: code = gatekeeper
            elif '--find' in command: out = '/fixture/tool'
            else: raise AssertionError(command)
            return {'exitCode': code, 'stdout': out, 'stderr': error}
        return call

    def test_default_signed_only_keeps_trust_checks_independent(self):
        report = readiness.evaluate(self.app, checker=self.checker())
        self.assertEqual(report['distributionPolicy'], 'signed-only')
        self.assertTrue(report['localSignatureVerified'])
        self.assertTrue(report['signatureReady'])
        self.assertTrue(report['distributionReady'])
        self.assertFalse(report['notarizationVerified'])
        self.assertFalse(report['gatekeeperAccepted'])
        self.assertFalse(report['notarizedDistributionReady'])
        self.assertEqual(report['blockers'], [])
        self.assertEqual(report['validCodeSigningIdentityCount'], 0, 'Validation of an already signed artifact does not require its private key')
        self.assertFalse(report['notarizationCredentialsVerified'])
        self.assertFalse(any(value in command for command in self.commands for value in ('submit', 'staple', 'store-credentials')))

    def test_notarized_policy_retains_previous_strict_requirements(self):
        report = readiness.evaluate(self.app, distribution_policy='notarized', checker=self.checker())
        self.assertTrue(report['signatureReady'])
        self.assertFalse(report['distributionReady'])
        self.assertEqual(len(report['blockers']), 2)
        complete = readiness.evaluate(self.app, distribution_policy='notarized', checker=self.checker(ticket=0, gatekeeper=0))
        self.assertTrue(complete['distributionReady'])
        self.assertTrue(complete['notarizedDistributionReady'])

    def test_adhoc_is_never_developer_id_ready_under_either_policy(self):
        for policy in ('signed-only', 'notarized'):
            with self.subTest(policy=policy):
                report = readiness.evaluate(self.app, distribution_policy=policy,
                                            checker=self.checker(mode='adhoc', ticket=0, gatekeeper=0))
                self.assertTrue(report['localPackageVerified'])
                self.assertTrue(report['localSignatureVerified'])
                self.assertEqual(report['signMode'], 'adhoc')
                self.assertFalse(report['signatureReady'])
                self.assertFalse(report['distributionReady'])

    def test_failed_integrity_or_signature_inspection_blocks_signed_only(self):
        for options in ({'package': 1}, {'signature': 1}, {'mode': 'unknown'}):
            with self.subTest(options=options):
                report = readiness.evaluate(self.app, checker=self.checker(**options))
                self.assertFalse(report['localSignatureVerified'])
                self.assertFalse(report['signatureReady'])
                self.assertFalse(report['distributionReady'])
                self.assertTrue(report['blockers'])

    def test_cli_default_and_explicit_notarized_policy_exit_codes(self):
        report_file = Path(self.tmp.name) / 'report.json'
        with contextlib.redirect_stdout(io.StringIO()):
            result = readiness.main([str(self.app), '--require-distribution', '--report', str(report_file)], checker=self.checker())
            strict = readiness.main([str(self.app), '--require-distribution', '--policy', 'notarized'], checker=self.checker())
            local_adhoc = readiness.main([str(self.app)], checker=self.checker(mode='adhoc'))
            require_adhoc = readiness.main([str(self.app), '--require-distribution'], checker=self.checker(mode='adhoc'))
        self.assertEqual((result, strict, local_adhoc, require_adhoc), (0, 1, 0, 1))
        self.assertEqual(json.loads(report_file.read_text())['distributionPolicy'], 'signed-only')

    def test_invalid_policy_and_report_inside_app_fail_before_commands(self):
        for arguments in ([str(self.app), '--policy', 'unsigned'], [str(self.app), '--report', str(self.app / 'report.json')]):
            with self.subTest(arguments=arguments), contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                readiness.main(arguments, checker=self.checker())
            self.assertEqual(error.exception.code, 2)
        self.assertEqual(self.commands, [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
