#!/usr/bin/env python3
"""Real production Worker retention regressions; no external HTTP or test-only APIs."""
import hashlib
import http.server
import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest

if len(sys.argv) < 2:
    raise SystemExit('usage: test_worker_retention.py /absolute/Worker [unittest options]')
executable = str(Path(sys.argv.pop(1)).resolve())
os.environ['JAVMETALITE_TEST_WORKER'] = executable
saved_argv = sys.argv
sys.argv = [sys.argv[0]]
spec = importlib.util.spec_from_file_location('retention_protocol_harness', Path(__file__).with_name('test_worker.py'))
harness = importlib.util.module_from_spec(spec)
spec.loader.exec_module(harness)
sys.argv = saved_argv


class RetentionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite retention 合成 ')
        self.addCleanup(self.temp.cleanup)

    def snapshot(self, worker):
        result = worker.request('workspace.getSnapshot')['result']['retention']
        limits = result['limits']
        self.assertLessEqual(result['completedOperations'], limits['completedOperations'])
        self.assertLessEqual(result['operationReceiptBytes'], limits['operationReceiptBytes'])
        self.assertLessEqual(result['pendingPreviews'], limits['pendingPreviews'])
        self.assertLessEqual(result['pendingPreviewBytes'], limits['pendingPreviewBytes'])
        return result

    def reason(self, response, expected):
        self.assertEqual(expected, response['error']['data']['reason'], response)

    def duplicate_imports(self, worker, video, count):
        ids = []
        for _ in range(count):
            operation_id = worker.request('workspace.importInputs', {'paths': [str(video)]})['result']['operationId']
            done = worker.operation(operation_id)
            self.assertEqual('completed', done['status'], done)
            self.assertEqual([], done['result']['importedJobIds'])
            ids.append(operation_id)
            worker.events.clear()  # Driver history is not part of Worker retention.
        return ids

    def test_01_recent_execute_receipt_is_idempotent_evicted_pair_fails_closed(self):
        with harness.Worker(self.temp.name, executable=executable) as worker:
            video, job = worker.import_video()
            plan = worker.preview(job)
            started = worker.request('save.execute', {'planId': plan['planId']})['result']
            done = worker.operation(started['operationId'])
            self.assertEqual('completed', done['result']['outcome'], done)
            nfo = video.with_suffix('.nfo')
            before = (hashlib.sha256(nfo.read_bytes()).hexdigest(), nfo.stat().st_mtime_ns)
            self.assertEqual(started, worker.request('save.execute', {'planId': plan['planId']})['result'])
            limits = self.snapshot(worker)['limits']
            recent = self.duplicate_imports(worker, video, limits['completedOperations'] + 2)
            retained = self.snapshot(worker)
            self.assertEqual(0, retained['executedPreviews'])
            self.reason(worker.request('operations.get', started), 'OPERATION_NOT_FOUND')
            self.reason(worker.request('save.execute', {'planId': plan['planId']}), 'PLAN_STALE')
            self.assertEqual('completed', worker.request('operations.get', {'operationId': recent[-1]})['result']['status'])
            self.assertEqual(before, (hashlib.sha256(nfo.read_bytes()).hexdigest(), nfo.stat().st_mtime_ns))
            self.assertEqual(1, len(worker.request('workspace.getSnapshot')['result']['jobs']))

    def test_02_preview_count_bounded_latest_remains_executable_old_token_cannot_write(self):
        with harness.Worker(self.temp.name, executable=executable) as worker:
            video, job = worker.import_video(nfo=None)
            count = self.snapshot(worker)['limits']['pendingPreviews'] + 5
            plans = []
            for _ in range(count):
                job = worker.get_job(job['jobId'])
                plans.append(worker.preview(job)['planId'])
            retained = self.snapshot(worker)
            self.assertEqual(retained['limits']['pendingPreviews'], retained['pendingPreviews'])
            self.reason(worker.request('save.execute', {'planId': plans[0]}), 'PLAN_STALE')
            self.assertFalse(video.with_suffix('.nfo').exists())
            started = worker.request('save.execute', {'planId': plans[-1]})['result']
            done = worker.operation(started['operationId'])
            self.assertEqual('completed', done['result']['outcome'], done)
            self.assertEqual(started, worker.request('save.execute', {'planId': plans[-1]})['result'])

    def test_03_running_read_only_operation_survives_completed_history_eviction(self):
        release = threading.Event()
        requested = threading.Event()

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                self.send_response(200)
                self.send_header('Content-Length', '4096')
                self.end_headers()
                self.wfile.write(b'x'); self.wfile.flush()
                requested.set()
                release.wait(15)

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        server.daemon_threads = True
        thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True)
        thread.start()
        try:
            with harness.Worker(self.temp.name, executable=executable) as worker:
                video, job = worker.import_video()
                html = (harness.ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace(
                    'https://example.invalid/syntheticpl.jpg', f'http://127.0.0.1:{server.server_port}/cover.png')
                started = worker.request('browser.importPage', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
                    'source': 'javlibrary', 'url': 'https://www.javlibrary.com/cn/?v=synthetic', 'html': html})['result']
                job = worker.operation(started['operationId'])['result']['job']
                artwork = next(a for a in job['artwork'] if a['kind'] == 'fanart' and a['source'] == 'javlibrary')
                active = worker.request('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': artwork['artworkId'], 'async': True})['result']
                self.assertTrue(requested.wait(3))
                limits = self.snapshot(worker)['limits']
                self.duplicate_imports(worker, video, limits['completedOperations'] + 2)
                self.assertEqual('running', worker.request('operations.get', active)['result']['status'])
                self.assertEqual(1, self.snapshot(worker)['protectedOperations'])
                worker.request('operations.cancel', active)
                self.assertEqual('canceled', worker.operation(active['operationId'], timeout=3)['status'])
                self.assertEqual(job['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
        finally:
            release.set(); server.shutdown(); server.server_close(); thread.join(timeout=2)

    def test_04_large_completed_results_hit_byte_budget_before_count_limit(self):
        with harness.Worker(self.temp.name, executable=executable) as worker:
            video, job = worker.import_video(nfo=None)
            original = video.read_bytes()
            # A real registered page parser creates large review snapshots. No test-only
            # result injection or remote request is needed to exercise the byte budget.
            html = (harness.ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace(
                'Synthetic browser title', 'Synthetic ' + 'x' * 300000)
            limits = self.snapshot(worker)['limits']
            operation_ids = []
            for _ in range(80):
                started = worker.request('browser.importPage', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
                    'source': 'javlibrary', 'url': 'https://www.javlibrary.com/cn/?v=synthetic', 'html': html})['result']
                done = worker.operation(started['operationId'])
                self.assertEqual('completed', done['status'], done)
                job = done['result']['job']
                operation_ids.append(started['operationId'])
                retained = self.snapshot(worker)
                worker.events.clear()
                if len(operation_ids) > 1 and 'error' in worker.request('operations.get', {'operationId': operation_ids[0]}):
                    break
            self.assertLess(len(operation_ids), limits['completedOperations'])
            self.reason(worker.request('operations.get', {'operationId': operation_ids[0]}), 'OPERATION_NOT_FOUND')
            self.assertGreater(retained['operationReceiptBytes'], 1024 * 1024)
            self.assertEqual(original, video.read_bytes())
            self.assertFalse(video.with_suffix('.nfo').exists())

    def test_05_large_pending_previews_hit_byte_budget_before_count_limit(self):
        with harness.Worker(self.temp.name, executable=executable) as worker:
            directory = Path(self.temp.name)
            # Long legal paths amplify actual preview DTOs without oversized media.
            for index in range(6):
                directory /= str(index) + 'x' * 99
            directory.mkdir(parents=True)
            paths = [directory / f'DEMO-{20000 + index}.mp4' for index in range(150)]
            for path in paths: path.write_bytes(b'SYNTHETIC')
            ids = worker.import_paths(paths)['importedJobIds']
            jobs = [worker.get_job(job_id) for job_id in ids]
            params = {'jobIds': ids, 'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in jobs}}
            limits = self.snapshot(worker)['limits']
            first_plan = None
            for count in range(1, limits['pendingPreviews'] + 1):
                response = worker.request('save.preview', params)
                self.assertIn('result', response, response)
                plan = response['result']
                self.assertTrue(plan['canExecute'], plan)
                first_plan = first_plan or plan['planId']
                retained = self.snapshot(worker)
                if retained['pendingPreviews'] < count: break
            self.assertLess(count, limits['pendingPreviews'])
            self.reason(worker.request('save.execute', {'planId': first_plan}), 'PLAN_STALE')
            self.assertEqual(150, len(list(directory.iterdir())))
            self.assertTrue(all(path.read_bytes() == b'SYNTHETIC' for path in paths))


if __name__ == '__main__':
    unittest.main(verbosity=2)
