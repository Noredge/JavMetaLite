import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from check_docs import check

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('worker_suite_runner', ROOT / 'scripts/macos/run_worker_suites.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class DocumentTests(unittest.TestCase):
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
                self.assertTrue(expected.issubset(names))
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
