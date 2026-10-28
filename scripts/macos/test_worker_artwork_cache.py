#!/usr/bin/env python3
"""Artwork disk-cache lifetime/leases, using synthetic media and loopback only.
Production-only cases also run against the extracted shipping Worker. Tiny budget
injection is accepted solely by the separate non-publishable regression host.
"""
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import uuid

if len(sys.argv) < 2:
    raise SystemExit('usage: test_worker_artwork_cache.py /absolute/Worker [--production-only|--require-search-host]')
EXECUTABLE = str(Path(sys.argv.pop(1)).resolve())
PRODUCTION_ONLY = '--production-only' in sys.argv
if PRODUCTION_ONLY: sys.argv.remove('--production-only')
HOST = None if PRODUCTION_ONLY else os.environ.get('JAVMETALITE_SEARCH_TEST_HOST')
if '--require-search-host' in sys.argv:
    sys.argv.remove('--require-search-host')
    if not HOST or not Path(HOST).is_file():
        raise SystemExit('Artwork budget tests require JAVMETALITE_SEARCH_TEST_HOST.')
saved_argv = sys.argv
sys.argv = [sys.argv[0], EXECUTABLE]
spec = importlib.util.spec_from_file_location('artwork_cache_protocol_harness', Path(__file__).with_name('test_worker.py'))
harness = importlib.util.module_from_spec(spec); spec.loader.exec_module(harness)
sys.argv = saved_argv


class ArtworkCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite artwork cache 合成 ')
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)

    def local_job(self, worker, index=1, shared=False):
        directory = self.base / 'synthetic media' / str(index)
        directory.mkdir(parents=True, exist_ok=True)
        video = directory / f'TEST-{700+index}.mp4'
        video.write_bytes(b'SYNTHETIC VIDEO ONLY')
        image = directory / f'TEST-{700+index}-poster.png'
        image.write_bytes(harness.synthetic_png(30 if shared else 30+index, 40))
        result = worker.import_paths([video])
        job = worker.get_job(result['importedJobIds'][0])
        art = next(a for a in job['artwork'] if a['kind'] == 'poster')
        return job, art, image

    def preview(self, worker, job, art, lease=None, asynchronous=False):
        params = {'jobId':job['jobId'], 'artworkId':art['artworkId'], 'async':asynchronous}
        if lease is not None: params['leaseId'] = lease
        return worker.request('artwork.getPreview', params)

    def cache(self, worker):
        state = worker.request('workspace.getSnapshot')['result']['artworkCache']
        self.assertLessEqual(state['bytes'], state['limits']['maximumBytes'])
        self.assertLessEqual(state['files'], state['limits']['maximumFiles'])
        self.assertLessEqual(state['leases'], state['limits']['maximumLeases'])
        return state

    def release(self, worker, lease):
        self.assertEqual({'released':True,'leaseId':lease}, worker.request('artwork.releasePreview',{'leaseId':lease})['result'])

    def test_01_shared_file_survives_job_removal_until_last_reader_releases(self):
        with harness.Worker(self.base, executable=EXECUTABLE) as worker:
            self.assertTrue(worker.initialized['capabilities']['artworkPreviewLeases'])
            first, art1, _ = self.local_job(worker, 1, shared=True)
            second, art2, _ = self.local_job(worker, 2, shared=True)
            one = self.preview(worker, first, art1, 'reader-one')['result']
            two = self.preview(worker, second, art2, 'reader-two')['result']
            self.assertEqual('reader-one', one['leaseId'])
            self.assertEqual(one['path'], two['path'])
            path = Path(one['path']); original = path.read_bytes()
            duplicate = self.preview(worker, first, art1, 'reader-one')
            self.assertEqual('INVALID_PARAMS', duplicate['error']['data']['reason'])
            worker.request('jobs.remove', {'jobIds':[first['jobId'],second['jobId']]})
            self.assertEqual(original,path.read_bytes())
            self.assertEqual(2,self.cache(worker)['leases'])
            self.release(worker, 'reader-one'); self.release(worker, 'reader-one')
            self.assertEqual(original,path.read_bytes())
            self.release(worker, 'reader-two')
            self.assertFalse(path.exists())
            self.assertEqual(0,self.cache(worker)['bytes'])
            cache_root = Path(worker.initialized['cacheRoot'])
        self.assertFalse(cache_root.exists())

    def test_02_legacy_preview_is_protected_until_its_job_is_removed(self):
        with harness.Worker(self.base, executable=EXECUTABLE) as worker:
            job, art, _ = self.local_job(worker)
            path=Path(self.preview(worker,job,art)['result']['path'])
            self.assertEqual(0,self.cache(worker)['leases'])
            self.assertEqual(1,self.cache(worker)['protectedFiles'])
            worker.request('jobs.remove',{'jobId':job['jobId']})
            self.assertFalse(path.exists())

    def test_03_release_before_download_result_cancels_without_late_publication(self):
        requested, finish = threading.Event(), threading.Event()
        payload = harness.synthetic_png(30,40)
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args): pass
            def do_GET(self):
                self.send_response(200); self.send_header('Content-Length',str(len(payload))); self.end_headers()
                self.wfile.write(payload[:1]); self.wfile.flush(); requested.set()
                finish.wait(10)
                try: self.wfile.write(payload[1:])
                except (BrokenPipeError,ConnectionResetError): pass
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler); server.daemon_threads=True
        thread=threading.Thread(target=lambda:server.serve_forever(poll_interval=.02),daemon=True); thread.start()
        try:
            with harness.Worker(self.base,executable=EXECUTABLE) as worker:
                _,job=worker.import_video()
                html=(harness.ROOT/'fixtures/macos/javlibrary-detail.html').read_text().replace(
                    'https://example.invalid/syntheticpl.jpg',f'http://127.0.0.1:{server.server_port}/cover.png')
                started=worker.request('browser.importPage',{'jobId':job['jobId'],'expectedReviewRevision':job['reviewRevision'],
                    'source':'javlibrary','url':'https://www.javlibrary.com/cn/?v=synthetic','html':html})['result']
                job=worker.operation(started['operationId'])['result']['job']
                art=next(a for a in job['artwork'] if a['kind']=='fanart' and a['source']=='javlibrary')
                active=self.preview(worker,job,art,'canceled-before-result',True)['result']
                self.assertTrue(requested.wait(3))
                self.release(worker,'canceled-before-result')
                self.assertEqual('canceled',worker.operation(active['operationId'],timeout=3)['status'])
                self.assertEqual(0,self.cache(worker)['files']); self.assertEqual(0,self.cache(worker)['leases'])
                finish.set()
                self.assertEqual(job['reviewRevision'],worker.get_job(job['jobId'])['reviewRevision'])
        finally:
            finish.set(); harness.stop_http_server(server, thread)

    def test_04_only_inactive_owned_sessions_are_reclaimed_recovery_untouched(self):
        data=self.base/'engine data 数据'; recovery=data/'recovery'; recovery.mkdir(parents=True)
        sentinel=recovery/'preserve.json'; sentinel.write_text('{"status":"completed","private":"keep exact bytes"}')
        fingerprint=hashlib.sha256(sentinel.read_bytes()).hexdigest()
        with harness.Worker(self.base,executable=EXECUTABLE) as active:
            job,art,_=self.local_job(active,1)
            active_path=Path(self.preview(active,job,art,'active-reader')['result']['path'])
            parent=Path(active.initialized['cacheRoot']).parent
            legacy=parent/uuid.uuid4().hex; legacy.mkdir(); (legacy/'old-version-data').write_text('legacy remains')
            foreign=parent/'user-folder'; foreign.mkdir(); (foreign/'notes').write_text('foreign remains')
            external=self.base/'external'; external.mkdir(); external_file=external/'do-not-delete'; external_file.write_text('outside')
            alias=parent/uuid.uuid4().hex; alias.symlink_to(external,target_is_directory=True)
            unsafe=harness.Worker(self.base,executable=EXECUTABLE)
            try:
                unsafe_root=Path(unsafe.initialized['cacheRoot'])
                unsafe.proc.kill(); unsafe.proc.wait(timeout=5)
            finally: unsafe.close()
            (unsafe_root/'outside-link').symlink_to(external_file)
            crashed=harness.Worker(self.base,executable=EXECUTABLE)
            try:
                job,art,_=self.local_job(crashed,2)
                orphan=Path(self.preview(crashed,job,art,'orphan-reader')['result']['path'])
                orphan_root=Path(crashed.initialized['cacheRoot'])
                crashed.proc.kill(); crashed.proc.wait(timeout=5)
            finally: crashed.close()
            self.assertTrue(orphan.exists())
            with harness.Worker(self.base,executable=EXECUTABLE) as next_worker:
                self.assertFalse(orphan_root.exists())
                self.assertTrue(active_path.exists(), 'A second active Worker must keep its cache.')
                self.assertTrue(unsafe_root.exists()); self.assertTrue((unsafe_root/'outside-link').is_symlink())
                self.assertTrue((legacy/'old-version-data').exists()); self.assertTrue((foreign/'notes').exists())
                self.assertTrue(alias.is_symlink()); self.assertEqual('outside',external_file.read_text())
                cleanup=self.cache(next_worker)['startupCleanup']
                self.assertGreaterEqual(cleanup['removedSessions'],1)
                self.assertGreaterEqual(cleanup['activeSessions'],1)
                self.assertGreaterEqual(cleanup['unownedSessions'],2)
                self.assertGreaterEqual(cleanup['unsafeSessions'],2)
                self.assertEqual(fingerprint,hashlib.sha256(sentinel.read_bytes()).hexdigest())
            self.assertTrue(active_path.exists())

    def assert_missing_payload_rebuilt(self, worker):
        job, art, source = self.local_job(worker, 40)
        first = self.preview(worker, job, art, 'original-reader')['result']
        path = Path(first['path']); original = path.read_bytes()
        path.unlink()  # Generated cache only, simulating system/user cache clearing.
        rebuilt = self.preview(worker, job, art, 'new-reader')['result']
        self.assertEqual(first['path'], rebuilt['path'])
        self.assertEqual(original, path.read_bytes())
        state = self.cache(worker)
        self.assertEqual(len(original), state['bytes']); self.assertEqual(1, state['files'])
        self.assertEqual(2, state['leases'])
        worker.request('jobs.remove', {'jobId':job['jobId']})
        self.release(worker, 'new-reader')
        self.assertEqual(original, path.read_bytes(), 'Original lease must still protect the regenerated payload.')
        self.release(worker, 'original-reader'); self.assertFalse(path.exists())
        # A replaced cache path must fail closed without altering an external file.
        other, other_art, _ = self.local_job(worker, 41)
        response = self.preview(worker, other, other_art, 'before-replacement')['result']
        target = Path(response['path']); target.unlink(); target.symlink_to(source)
        expected = source.read_bytes()
        rejected = self.preview(worker, other, other_art, 'after-replacement')
        self.assertIn('error', rejected)
        self.assertTrue(target.is_symlink()); self.assertEqual(expected, source.read_bytes())

    def test_05_missing_generated_payload_is_rebuilt_without_losing_existing_leases(self):
        with harness.Worker(self.base, executable=EXECUTABLE) as worker:
            self.assert_missing_payload_rebuilt(worker)


@unittest.skipUnless(HOST, 'Budget injection is confined to the separate fixed-response host')
class ArtworkBudgetTests(ArtworkCacheTests):
    # Do not inherit/repeat production cases against the host.
    test_01_shared_file_survives_job_removal_until_last_reader_releases = None
    test_02_legacy_preview_is_protected_until_its_job_is_removed = None
    test_03_release_before_download_result_cancels_without_late_publication = None
    test_04_only_inactive_owned_sessions_are_reclaimed_recovery_untouched = None
    test_05_missing_generated_payload_is_rebuilt_without_losing_existing_leases = None

    def host(self, limits, name):
        directory=self.base/name; directory.mkdir()
        config=directory/'fixed-cache.json'; config.write_text(json.dumps({'artworkCacheLimits':limits}))
        return harness.Worker(directory,executable=HOST,arguments=['--data-root',str(directory/'data'),'--fixture-config',str(config)])

    def test_05_byte_and_file_budgets_reject_when_pinned_then_evict_only_released(self):
        for limit in ('bytes','files'):
            with self.subTest(limit=limit):
                limits={'maximumBytes':180 if limit=='bytes' else 1024*1024,'maximumFiles':1024 if limit=='bytes' else 1,'maximumLeases':10}
                with self.host(limits,limit) as worker:
                    one,art1,_=self.local_job(worker,10 if limit=='bytes' else 20)
                    two,art2,_=self.local_job(worker,11 if limit=='bytes' else 21)
                    first=self.preview(worker,one,art1,'first')['result']; path=Path(first['path']); expected=path.read_bytes()
                    failed=self.preview(worker,two,art2,'second')
                    self.assertEqual('ARTWORK_CACHE_CAPACITY',failed['error']['data']['reason'])
                    self.assertEqual(expected,path.read_bytes())
                    self.assertEqual(1,self.cache(worker)['leases'])
                    self.release(worker,'first')
                    accepted=self.preview(worker,two,art2,'second')['result']
                    self.assertTrue(Path(accepted['path']).exists()); self.assertFalse(path.exists())
                    state=self.cache(worker); self.assertEqual(1,state['files'])
                    disk=list(Path(worker.initialized['cacheRoot']).glob('*.png'))+list(Path(worker.initialized['cacheRoot']).glob('*.jpg'))
                    self.assertEqual(state['bytes'],sum(p.stat().st_size for p in disk))
                    self.assertEqual([],list(Path(worker.initialized['cacheRoot']).glob('*.tmp')))

    def test_06_lease_count_has_separate_admission_limit(self):
        with self.host({'maximumBytes':1024*1024,'maximumFiles':20,'maximumLeases':1},'leases') as worker:
            one,art1,_=self.local_job(worker,30)
            first=self.preview(worker,one,art1,'one')['result']
            rejected=self.preview(worker,one,art1,'two')
            self.assertEqual('ARTWORK_CACHE_CAPACITY',rejected['error']['data']['reason'])
            self.assertTrue(Path(first['path']).exists())
            self.release(worker,'one')
            self.assertIn('result',self.preview(worker,one,art1,'two'))
            self.assertEqual(1,self.cache(worker)['leases'])

    def test_07_missing_payload_rebuild_respects_one_file_budget(self):
        with self.host({'maximumBytes':180,'maximumFiles':1,'maximumLeases':10},'regenerate') as worker:
            self.assert_missing_payload_rebuilt(worker)


if __name__=='__main__': unittest.main(verbosity=2, testRunner=harness.TimedRunner)
