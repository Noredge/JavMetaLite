#!/usr/bin/env python3
"""Failure-oriented release provenance/CLI tests on disposable filesystem fixtures."""
import json
import base64
import hashlib
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from release_manifest import (BUILD_MANIFEST, canonical_bytes, digest, inventory, payload_manifest,
                              package_hashes, safe_relative, source_inputs, verify_payload)
from verify_package import declared_executable

HERE = Path(__file__).resolve().parent


class ReleaseManifestTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite manifest 测试 ')
        self.root = Path(self.temp.name)
        self.app = self.root / 'Synthetic.app'
        self.payload = self.app / 'Contents/MacOS/fixture'
        self.payload.parent.mkdir(parents=True)
        self.payload.write_bytes(b'synthetic payload')
        self.payload.chmod(0o755)
        self.build = self.app / BUILD_MANIFEST
        self.build.parent.mkdir(parents=True)
        self.build.write_bytes(canonical_bytes({'schemaVersion': 1, 'kind': 'build-provenance'}))

    def tearDown(self):
        self.temp.cleanup()

    def test_canonical_manifest_and_copy_are_stable(self):
        first = payload_manifest(self.app)
        copy = self.root / 'copy.app'
        shutil.copytree(self.app, copy)
        verify_payload(copy, first)
        self.assertEqual(canonical_bytes(first), canonical_bytes(payload_manifest(self.app)))
        self.assertEqual(canonical_bytes({'b': 2, 'a': 1}), canonical_bytes({'a': 1, 'b': 2}))

    def test_same_size_external_edit_is_rejected(self):
        manifest = payload_manifest(self.app)
        self.payload.write_bytes(b'synthetic payloaD')
        with self.assertRaisesRegex(ValueError, 'payload mismatch'):
            verify_payload(self.app, manifest)

    def test_missing_extra_and_executable_mode_changes_are_rejected(self):
        manifest = payload_manifest(self.app)
        self.payload.chmod(0o644)
        with self.assertRaises(ValueError):
            verify_payload(self.app, manifest)
        self.payload.chmod(0o755)
        extra = self.app / 'Contents/extra'
        extra.write_text('unexpected')
        with self.assertRaises(ValueError):
            verify_payload(self.app, manifest)
        extra.unlink()
        self.payload.unlink()
        with self.assertRaises(ValueError):
            verify_payload(self.app, manifest)

    def test_escaping_symlink_and_special_file_are_refused(self):
        outside = self.root / 'outside'
        outside.write_text('private')
        link = self.app / 'Contents/link'
        link.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'symlink escapes'):
            inventory(self.app)
        link.unlink()
        os.mkfifo(link)
        with self.assertRaisesRegex(ValueError, 'Unsupported bundle entry'):
            inventory(self.app)

    def test_internal_symlink_destination_is_part_of_snapshot(self):
        link = self.app / 'Contents/MacOS/link'
        link.symlink_to('fixture')
        manifest = payload_manifest(self.app)
        self.assertEqual(manifest['payload']['Contents/MacOS/link']['target'], 'fixture')
        verify_payload(self.app, manifest)

    def test_archive_changes_are_rejected(self):
        archive = self.root / 'archive.zip'
        archive.write_bytes(b'archive bytes')
        manifest = payload_manifest(self.app, archive)
        verify_payload(self.app, manifest, archive)
        archive.write_bytes(b'changed bytes')
        with self.assertRaisesRegex(ValueError, 'Archive content'):
            verify_payload(self.app, manifest, archive)

    def test_generic_signature_attribute_loss_is_detected(self):
        subprocess.run(['/usr/bin/codesign', '--force', '--sign', '-', str(self.payload)], check=True, capture_output=True)
        manifest = payload_manifest(self.app)
        original_bytes = digest(self.payload)
        self.assertIn('com.apple.cs.CodeDirectory', manifest['payload']['Contents/MacOS/fixture']['signatureAttributes'])
        subprocess.run(['/usr/bin/xattr', '-d', 'com.apple.cs.CodeDirectory', str(self.payload)], check=True)
        self.assertEqual(digest(self.payload), original_bytes)
        with self.assertRaisesRegex(ValueError, 'payload mismatch'):
            verify_payload(self.app, manifest)

    def test_manifest_path_traversal_is_refused(self):
        for value in ('../outside', '/tmp/outside', 'Contents/../outside', './Contents/file', ''):
            with self.subTest(value=value), self.assertRaises(ValueError):
                safe_relative(value)

    def test_declared_executable_cannot_redirect_launch(self):
        self.assertEqual(declared_executable(self.app, {'CFBundleExecutable': 'fixture'}, 'fixture'), self.payload)
        for value in ('../fixture', str(self.payload), '/bin/sh', 'other', '', None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                declared_executable(self.app, {'CFBundleExecutable': value}, 'fixture')
        outside = self.root / 'outside'; self.payload.rename(outside)
        self.payload.symlink_to(outside)
        with self.assertRaises(ValueError):
            declared_executable(self.app, {'CFBundleExecutable': 'fixture'}, 'fixture')

    def test_python_optimization_does_not_disable_verification(self):
        path = self.root / 'manifest.json'
        path.write_bytes(canonical_bytes(payload_manifest(self.app)))
        self.payload.write_text('tampered')
        result = subprocess.run([sys.executable, '-O', str(HERE / 'release_manifest.py'), 'verify',
                                 '--app', str(self.app), '--manifest', str(path)], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('payload mismatch', result.stderr)

    def test_source_export_without_git_records_exact_inputs(self):
        source = self.root / 'source'
        file = source / 'macos/Package.swift'
        file.parent.mkdir(parents=True)
        file.write_text('synthetic source')
        manifest = {'schemaVersion': 1, 'kind': 'source-export',
                    'source': {'gitCommit': 'a' * 40, 'dirtyWorkingTree': True},
                    'files': {'macos/Package.swift': {'sha256': digest(file), 'mode': stat.S_IMODE(file.stat().st_mode)}}}
        (source / 'SOURCE-MANIFEST.json').write_bytes(canonical_bytes(manifest))
        actual = source_inputs(source)
        self.assertEqual(actual['origin'], 'source-export')
        self.assertTrue(actual['verifiedExport'])
        self.assertEqual(actual['files']['macos/Package.swift']['sha256'], digest(file))
        (source / 'macos/.build').mkdir()
        (source / 'macos/.build/generated').write_text('not a source input')
        self.assertEqual(source_inputs(source)['inputDigest'], actual['inputDigest'])
        file.write_text('intentional edited source')
        with self.assertRaisesRegex(ValueError, 'Source export changed'):
            source_inputs(source)
        changed = source_inputs(source, allow_modified_export=True)
        self.assertFalse(changed['verifiedExport'])
        self.assertEqual(changed['intentionalExportChanges'], ['macos/Package.swift'])
        new = source / 'macos/new.swift'
        new.write_text('new source')
        with self.assertRaises(ValueError):
            source_inputs(source)

    def test_unknown_or_unsigned_cli_modes_fail_before_build(self):
        cases = [('build.sh', '--sign-mode', 'unsigned'), ('package.sh', '--sign-mode', 'developer-id'),
                 ('package.sh', '--arch', 'invalid'), ('build.sh', '--sign-mode', 'adhoc', '--team-id', 'AAAAAAAAAA'),
                 ('package.sh', '--unknown')]
        for args in cases:
            with self.subTest(args=args):
                result = subprocess.run([str(HERE / args[0]), *args[1:]], capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn('Determining projects to restore', result.stdout)

    def test_cli_help_needs_no_sdk_or_signing_identity(self):
        for script in ('build.sh', 'package.sh', 'verify-package.sh'):
            with self.subTest(script=script):
                result = subprocess.run([str(HERE / script), '--help'], env={'PATH': '/usr/bin:/bin'},
                                        capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn('sign', result.stdout.lower())

    def test_nuget_content_and_archive_hashes_are_distinct_and_archive_is_checked(self):
        folder = self.root / 'package'; folder.mkdir()
        package = folder / 'synthetic.1.0.0.nupkg'; package.write_bytes(b'synthetic signed package')
        (folder / 'synthetic.1.0.0.nupkg.sha512').write_text(base64.b64encode(hashlib.sha512(package.read_bytes()).digest()).decode())
        (folder / '.nupkg.metadata').write_text(json.dumps({'contentHash': 'normalized-content-hash'}))
        hashes = package_hashes(folder, 'Synthetic', '1.0.0')
        self.assertEqual(hashes['contentHashSHA512'], 'normalized-content-hash')
        self.assertNotEqual(hashes['archiveHashSHA512'], hashes['contentHashSHA512'])
        package.write_bytes(b'tampered signed package')
        with self.assertRaisesRegex(ValueError, 'archive hash mismatch'):
            package_hashes(folder, 'Synthetic', '1.0.0')

    def test_source_zip_reexport_is_stable_and_runs_without_git(self):
        source = self.root / 'source'; scripts = source / 'scripts/macos'; scripts.mkdir(parents=True)
        for name in ('source-archive.py', 'release_manifest.py'):
            shutil.copy2(HERE / name, scripts / name)
        source_file = source / 'JavMetaLite.Fixture/File.cs'; source_file.parent.mkdir()
        source_file.write_text('// synthetic source only')
        files = {p.relative_to(source).as_posix(): {'sha256': digest(p), 'mode': stat.S_IMODE(p.stat().st_mode)}
                 for p in source.rglob('*') if p.is_file()}
        (source / 'SOURCE-MANIFEST.json').write_bytes(canonical_bytes({'schemaVersion': 1, 'kind': 'source-export',
            'source': {'gitCommit': 'a' * 40, 'dirtyWorkingTree': True}, 'files': files}))
        first, second = self.root / 'first.zip', self.root / 'second.zip'
        for output in (first, second):
            subprocess.run([sys.executable, str(scripts / 'source-archive.py'), str(output)], check=True, capture_output=True)
        self.assertEqual(digest(first), digest(second))
        blocked = subprocess.run([sys.executable, str(scripts / 'source-archive.py'), str(first)], capture_output=True)
        self.assertNotEqual(blocked.returncode, 0, 'Existing user output must not be overwritten implicitly')
        extracted = self.root / '中文 source extraction'; extracted.mkdir()
        subprocess.run(['/usr/bin/ditto', '-x', '-k', str(first), str(extracted)], check=True)
        checked = source_inputs(extracted / 'JavMetaLite')
        self.assertTrue(checked['verifiedExport'])
        self.assertEqual(checked['files']['JavMetaLite.Fixture/File.cs']['sha256'], digest(source_file))


if __name__ == '__main__':
    unittest.main(verbosity=2)
