#!/usr/bin/env python3
"""Real production Worker queue removal. Disposable synthetic bytes; no providers/UI."""
import hashlib
import http.server
import json
import os
from pathlib import Path
import tempfile
import threading
import time
import unittest
import test_worker as rpc


class QueueInteractionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite clear 合成 ')
        self.root = Path(self.temp.name)
        self.worker = rpc.Worker(self.root / 'engine')
        self.media = self.root / 'media'; self.media.mkdir()

    def tearDown(self):
        self.worker.close()
        self.temp.cleanup()

    def add(self, number, size=None):
        folder = self.media / ('input-' + number); folder.mkdir()
        video = folder / (number + '.mp4')
        video.write_bytes(b'SYNTHETIC QUEUE VIDEO ONLY')
        if size is not None:
            with video.open('r+b') as stream: stream.truncate(size)
        video.with_suffix('.nfo').write_text('<movie><title>Synthetic queue</title><num>' + number + '</num><unknown>KEEP</unknown><resume><position>1</position><total>2</total></resume></movie>')
        (folder / 'poster.png').write_bytes(rpc.synthetic_png(2, 3))
        result = self.worker.import_paths([video], recursive=False)
        self.assertEqual(1, len(result['importedJobIds']))
        return video, self.worker.get_job(result['importedJobIds'][0])

    def jobs(self):
        return self.worker.request('workspace.getSnapshot')['result']['jobs']

    def inventory(self):
        result = {}
        for path in self.media.rglob('*'):
            if path.is_file():
                with path.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
                st = path.stat(); result[str(path.relative_to(self.media))] = (digest, st.st_size, st.st_mtime_ns)
        return result

    def test_01_bulk_acknowledgement_legacy_compatibility_and_stale_plan(self):
        _, a = self.add('DEMO-901'); _, b = self.add('DEMO-902'); _, c = self.add('DEMO-903')
        before = self.inventory()
        preview = self.worker.preview(a, overwrite=True)
        result = self.worker.request('jobs.remove', {'jobIds': [a['jobId'], c['jobId']]})['result']
        self.assertTrue(result['removed']); self.assertEqual([a['jobId'], c['jobId']], result['removedIds']); self.assertEqual(2, result['removedCount'])
        self.assertEqual([b['jobId']], [j['jobId'] for j in self.jobs()])
        rejected = self.worker.request('save.execute', {'planId': preview['planId']})
        self.assertEqual('PLAN_STALE', rejected['error']['data']['reason'])
        legacy = self.worker.request('jobs.remove', {'jobId': b['jobId']})['result']
        self.assertTrue(legacy['removed']); self.assertEqual(1, legacy['removedCount']); self.assertEqual([], self.jobs())
        self.assertEqual(before, self.inventory(), 'Removing reviews cannot delete or rewrite video/NFO/artwork.')

    def test_02_all_ids_and_parameters_validate_before_any_removal(self):
        _, a = self.add('DEMO-911'); _, b = self.add('DEMO-912')
        before_jobs, before_media = self.jobs(), self.inventory()
        for params, reason in [
            ({'jobIds': [a['jobId'], 'missing-job']}, 'JOB_NOT_FOUND'),
            ({'jobIds': [a['jobId'], a['jobId']]}, 'INVALID_PARAMS'),
            ({'jobIds': []}, 'INVALID_PARAMS'),
            ({'jobIds': [a['jobId'], None]}, 'INVALID_PARAMS'),
            ({'jobIds': [a['jobId']], 'jobId': b['jobId']}, 'INVALID_PARAMS')]:
            self.assertEqual(reason, self.worker.request('jobs.remove', params)['error']['data']['reason'])
            self.assertEqual(before_jobs, self.jobs(), 'A later invalid ID must not clear an earlier valid job.')
            self.assertEqual(before_media, self.inventory())

    def test_03_busy_later_job_and_active_save_reject_the_complete_request(self):
        _, a = self.add('DEMO-921'); _, b = self.add('DEMO-922')
        before_media = self.inventory()
        received = threading.Event(); release = threading.Event()
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                self.send_response(200); self.send_header('Content-Type', 'image/png'); self.send_header('Content-Length', '4096'); self.end_headers()
                self.wfile.write(b'x'); self.wfile.flush(); received.set(); release.wait(15)
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler); server.daemon_threads = True
        thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True); thread.start()
        try:
            html = (rpc.ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace('DEMO-001', 'DEMO-922').replace('https://example.invalid/syntheticpl.jpg', f'http://127.0.0.1:{server.server_port}/syntheticpl.jpg')
            imported = self.worker.request('browser.importPage', {'jobId': b['jobId'], 'expectedReviewRevision': b['reviewRevision'],
                'source': 'javlibrary', 'url': 'https://www.javlibrary.com/cn/?v=synthetic', 'html': html})
            b = self.worker.operation(imported['result']['operationId'])['result']['job']
            plan = self.worker.preview(b, writeNfo=False, downloadFanart=True)
            self.assertTrue(plan['canExecute'], plan)
            op = self.worker.request('save.execute', {'planId': plan['planId']})['result']['operationId']
            self.assertTrue(received.wait(5), 'The actual production output downloader must receive a partial HTTP body before removal is tested.')
            self.assertEqual('running', self.worker.request('operations.get', {'operationId': op})['result']['status'])
            active_jobs = self.jobs()
            self.assertTrue(any(j['jobId'] == b['jobId'] and j['busy'] for j in active_jobs))
            self.assertTrue(any(j['jobId'] == a['jobId'] and not j['busy'] for j in active_jobs))
            for ids in [[a['jobId'], b['jobId']], [a['jobId']]]:
                self.assertEqual('JOB_BUSY', self.worker.request('jobs.remove', {'jobIds': ids})['error']['data']['reason'])
                self.assertEqual([a['jobId'], b['jobId']], [j['jobId'] for j in self.jobs()])
            self.worker.request('operations.cancel', {'operationId': op})
            terminal = self.worker.operation(op)
            self.assertEqual('completed', terminal['status'], terminal)
            self.assertEqual('canceled', terminal['result']['outcome'], terminal)
            self.assertEqual([], terminal['result']['savedJobIds'])
            self.assertEqual(before_media, self.inventory(), 'Canceled output and refused queue removal preserve video, original NFO, and local artwork bytes/mtime.')
        finally:
            release.set(); server.shutdown(); server.server_close(); thread.join(timeout=2)

    def test_04_one_thousand_reviews_clear_with_one_rpc_and_keep_all_inputs(self):
        paths = []
        for number in range(60000, 61000):
            path = self.media / f'DEMO-{number}.mp4'; path.write_bytes(b'SYNTHETIC QUEUE INPUT'); paths.append(path)
        started = self.worker.request('workspace.importInputs', {'paths': [str(p) for p in paths], 'recursive': False})['result']
        completed = self.worker.operation(started['operationId'], timeout=180)
        self.assertEqual('completed', completed['status']); imported = completed['result']
        ids = imported['importedJobIds']; self.assertEqual(1000, len(ids)); before = self.inventory()
        result = self.worker.request('jobs.remove', {'jobIds': ids})['result']
        self.assertEqual(1000, result['removedCount']); self.assertEqual(ids, result['removedIds']); self.assertEqual([], self.jobs())
        self.assertEqual(before, self.inventory())


if __name__ == '__main__': unittest.main(verbosity=2)
