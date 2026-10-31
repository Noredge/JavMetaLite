import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from scope import DOCS, PRESSURE_SENSITIVE, changes_for_event, classify, validation_level
from check_docs import check

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('worker_suite_runner', ROOT / 'scripts/macos/run_worker_suites.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ScopeTests(unittest.TestCase):
    def test_core_source_changes_and_risk_boundaries(self):
        for path in ['macos/JavMetaLiteMac/Features/EditorView.swift',
                     'macos/WorkerKit/WorkerClient.swift', 'JavMetaLite.Core/Models/MovieJob.cs',
                     'JavMetaLite.Worker/Saving.cs', 'JavMetaLite.UiSmokeTests/Program.cs']:
            with self.subTest(path=path):
                self.assertEqual('core', validation_level([('M', path), ('M', 'README.md')], 'push', {}))
                self.assertEqual('full', validation_level([('A', path)], 'push', {}))
                self.assertEqual('full', validation_level([('D', path)], 'push', {}))
        for path in [*PRESSURE_SENSITIVE, 'global.json', 'NuGet.Config',
                     'JavMetaLite.Worker/JavMetaLite.Worker.csproj', 'macos/Package.swift',
                     'macos/JavMetaLiteMac/Resources/Info.plist', 'docs/macos/PACKAGE.md',
                     'scripts/macos/package.sh', 'scripts/macos/test.sh',
                     '.github/workflows/macos.yml', 'contracts/protocol-v1.json', 'unknown.file']:
            with self.subTest(path=path):
                self.assertEqual('full', validation_level([('M', path)], 'push', {}))

    def test_tags_manual_and_unknown_events_cannot_silently_use_core(self):
        docs = [('M', 'README.md')]
        self.assertEqual('docs', validation_level(docs, 'push', {}))
        self.assertEqual('full', validation_level(docs, 'push', {'ref': 'refs/tags/macos-v0.4.0-preview.3'}))
        for requested, expected in [('core', 'core'), ('full', 'full'), ('docs', 'full'), ('bad', 'full')]:
            self.assertEqual(expected, validation_level([], 'workflow_dispatch', {'inputs': {'validation': requested}}))
        self.assertEqual('full', validation_level([], 'workflow_dispatch', {}))
        self.assertEqual('full', validation_level([], 'push', {}))
        self.assertEqual('full', validation_level(docs, 'unrecognized', {}))

    def test_only_edited_allowlisted_documents_are_fast(self):
        self.assertTrue(classify([('M', name) for name in DOCS]))
        for change in [('M', 'docs/macos/PACKAGE.md'), ('M', 'THIRD_PARTY_NOTICES.md'),
                       ('M', 'docs/macos/licenses/library.md'), ('M', '.github/workflows/macos.yml'),
                       ('M', 'macos/Package.swift'), ('M', 'scripts/macos/test.sh'),
                       ('D', 'README.md'), ('A', 'README.md'), ('R100', 'README.md')]:
            with self.subTest(change=change):
                self.assertFalse(classify([('M', 'README.md'), change]))
        self.assertFalse(classify([]))

    def test_manual_missing_and_invalid_base_require_full(self):
        for name, event in [('workflow_dispatch', {}), ('push', {'before': '0' * 40}),
                            ('push', {'before': '--output=private'}), ('pull_request', {})]:
            with self.subTest(name=name, event=event):
                changes, _ = changes_for_event(name, event, ROOT)
                self.assertFalse(classify(changes))

    def test_real_git_diff_and_unavailable_commit_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=root, stderr=subprocess.PIPE).decode().strip()
            git('init');git('config', 'user.name', 'Fixture');git('config', 'user.email', 'fixture@example.invalid')
            (root / 'README.md').write_text('first');git('add', '.');git('commit', '-m', 'fixture')
            base = git('rev-parse', 'HEAD')
            (root / 'README.md').write_text('second');git('commit', '-am', 'docs')
            self.assertTrue(classify(changes_for_event('push', {'before': base}, root)[0]))
            self.assertTrue(classify(changes_for_event('pull_request', {'pull_request': {'base': {'sha': base}}}, root)[0]))
            self.assertFalse(classify(changes_for_event('push', {'before': 'f' * 40}, root)[0]))

    def test_document_links_decode_unicode_and_fail_on_missing_target(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            subprocess.run(['git', 'init', '-q', tmp], check=True)
            (root / '中文.txt').write_text('fixture')
            (root / 'README.md').write_text('[ok](%E4%B8%AD%E6%96%87.txt) [bad](missing.md) [remote](https://example.invalid)')
            subprocess.run(['git', 'add', '.'], cwd=root, check=True)
            count, failures = check(root)
            self.assertEqual(2, count);self.assertEqual(1, len(failures))
            self.assertIn('missing.md', failures[0])


class RunnerTests(unittest.TestCase):
    def test_explicit_full_overrides_parent_core_environment(self):
        with tempfile.TemporaryDirectory() as tmp:
            report = runner.run_suites([('profile', [sys.executable, '-c',
                "import os; print(os.environ['JAVMETALITE_TEST_PROFILE'])"])], tmp,
                environment={**os.environ, 'JAVMETALITE_TEST_PROFILE': 'core'}, profile='full')
            self.assertEqual('full', Path(tmp, 'profile.log').read_text().strip())
            self.assertEqual('full', report['profile'])
            with self.assertRaises(ValueError): runner.run_suites([], tmp, profile='unknown')

    def test_final_package_refuses_core_profile_before_launch(self):
        with tempfile.TemporaryDirectory() as tmp:
            result = subprocess.run([sys.executable, str(ROOT / 'scripts/macos/run_worker_suites.py'),
                '--worker', '/unused-worker', '--mode', 'package', '--profile', 'core', '--log-dir', tmp],
                capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('requires the full profile', result.stderr)
            self.assertEqual([], list(Path(tmp).iterdir()))

    def test_core_excludes_only_three_named_pressure_cases_and_full_keeps_them(self):
        worker_dir = str(ROOT / 'scripts/macos')
        expected = {
            'QueueInteractionTests.test_04_one_thousand_reviews_clear_with_one_rpc_and_keep_all_inputs',
            'RetentionTests.test_04_large_completed_results_hit_byte_budget_before_count_limit',
            'RetentionTests.test_05_large_pending_previews_hit_byte_budget_before_count_limit'}
        sys.path.insert(0, worker_dir)
        try:
            for profile in ('core', 'full'):
                skipped = set(); names = set()
                for filename, cls in [('test_queue_interactions.py', 'QueueInteractionTests'),
                                      ('test_worker_retention.py', 'RetentionTests'),
                                      ('test_worker.py', 'WorkerTests'),
                                      ('test_worker.py', 'ProviderWorkerTests'),
                                      ('test_worker_artwork_cache.py', 'ArtworkCacheTests'),
                                      ('test_worker_artwork_cache.py', 'ArtworkBudgetTests')]:
                    with mock.patch.dict(os.environ, {'JAVMETALITE_TEST_PROFILE': profile,
                                                      'JAVMETALITE_SEARCH_TEST_HOST': sys.executable}), \
                         mock.patch.object(sys, 'argv', [filename, '/unused-worker']):
                        module_spec = importlib.util.spec_from_file_location('profile_' + profile + cls,
                                                  ROOT / 'scripts/macos' / filename)
                        module = importlib.util.module_from_spec(module_spec)
                        module_spec.loader.exec_module(module)
                    case = getattr(module, cls)
                    self.assertFalse(getattr(case, '__unittest_skip__', False))
                    for name in unittest.defaultTestLoader.getTestCaseNames(case):
                        qualified = cls + '.' + name; names.add(qualified)
                        if getattr(getattr(case, name), '__unittest_skip__', False): skipped.add(qualified)
                self.assertEqual(103, len(names))
                self.assertEqual(expected if profile == 'core' else set(), skipped)
        finally:
            sys.path.remove(worker_dir)

    def test_worker_shutdown_failure_is_not_a_successful_cleanup(self):
        worker_dir = str(ROOT / 'scripts/macos')
        sys.path.insert(0, worker_dir)
        try:
            with mock.patch.object(sys, 'argv', ['test_worker.py', '/unused-fixture-worker']):
                worker_spec = importlib.util.spec_from_file_location('shutdown_fixture',
                                    ROOT / 'scripts/macos/test_worker.py')
                module = importlib.util.module_from_spec(worker_spec)
                worker_spec.loader.exec_module(module)
            worker = module.Worker.__new__(module.Worker)
            worker.proc = mock.Mock()
            worker.proc.poll.return_value = None
            worker.proc.returncode = -9
            worker.reader = mock.Mock(); worker.errors = mock.Mock()
            worker.request = mock.Mock(side_effect=TimeoutError('synthetic shutdown timeout'))
            with self.assertRaisesRegex(AssertionError, 'forced termination'):
                worker.close()
            worker.proc.kill.assert_called_once()
        finally:
            sys.path.remove(worker_dir)

    def test_failure_preserves_other_results_logs_and_isolated_temp_roots(self):
        with tempfile.TemporaryDirectory() as tmp:
            code = "import os,pathlib,sys; print(os.environ['TMPDIR']); pathlib.Path(os.environ['TMPDIR'],'fixture').write_text('safe'); sys.exit(int(sys.argv[1]))"
            suites = [(name, [sys.executable, '-c', code, status]) for name, status in [('one', '1'), ('two', '0'), ('three', '0')]]
            report = runner.run_suites(suites, tmp, jobs=2)
            self.assertEqual([1, 0, 0], [x['exitCode'] for x in report['suites']])
            roots = [Path(tmp, name + '.log').read_text().strip() for name, _ in suites]
            self.assertEqual(3, len(set(roots)))
            self.assertTrue(all(not Path(name).exists() for name in roots))
            self.assertEqual(3, len(json.loads(Path(tmp, 'worker-suites.json').read_text())['suites']))

    def test_timeout_kills_suite_group_and_records_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            report = runner.run_suites([('stalled', [sys.executable, '-c', 'import time; time.sleep(30)'])],
                                       tmp, timeout=.1)
            self.assertEqual(124, report['suites'][0]['exitCode'])
            self.assertIn('process group terminated', Path(tmp, 'stalled.log').read_text())


if __name__ == '__main__':
    unittest.main(verbosity=2)
