import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from scope import DOCS, changes_for_event, classify
from check_docs import check

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('worker_suite_runner', ROOT / 'scripts/macos/run_worker_suites.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ScopeTests(unittest.TestCase):
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
