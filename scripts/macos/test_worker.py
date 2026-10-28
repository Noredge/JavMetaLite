#!/usr/bin/env python3
"""Real Worker subprocess regression tests. Synthetic media only; no live providers."""
import hashlib
import http.server
import json
import os
from pathlib import Path
import queue
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import xml.etree.ElementTree as ET
import zlib

EXECUTABLE = str(Path(sys.argv.pop(1)).resolve()) if len(sys.argv) > 1 else os.environ['JAVMETALITE_TEST_WORKER']
ROOT = Path(__file__).resolve().parents[2]
NFO = (ROOT / 'fixtures/macos/round-trip.nfo').read_text()
SEARCH_TEST_HOST = os.environ.get('JAVMETALITE_SEARCH_TEST_HOST')
PRODUCTION_ONLY = '--production-only' in sys.argv
if PRODUCTION_ONLY: sys.argv.remove('--production-only')
if '--require-search-host' in sys.argv:
    sys.argv.remove('--require-search-host')
    if not SEARCH_TEST_HOST or not Path(SEARCH_TEST_HOST).is_file():
        raise SystemExit('Complete protocol gate requires JAVMETALITE_SEARCH_TEST_HOST; fixed HTTP provider tests cannot be skipped.')


def synthetic_png(width, height):
    def chunk(kind, value): return struct.pack('>I', len(value)) + kind + value + struct.pack('>I', zlib.crc32(kind + value) & 0xffffffff)
    scanlines = (b'\0' + bytes((24, 80, 140)) * width) * height
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(scanlines)) + chunk(b'IEND', b'')


class Worker:
    def __init__(self, directory, initialize=True, stderr_bytes=0, executable=None, arguments=None, environment=None):
        self.directory = Path(directory)
        self.data = self.directory / 'engine data 数据'
        launch = arguments if arguments is not None else ['--data-root', str(self.data), '--diagnostic-stderr-bytes', str(stderr_bytes)]
        self.proc = subprocess.Popen([executable or EXECUTABLE, *launch], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment)
        self.messages = queue.Queue()
        self.responses = {}
        self.events = []
        self.stderr_count = 0
        self.next_id = 0
        self.reader = threading.Thread(target=self.read_stdout, daemon=True)
        self.errors = threading.Thread(target=self.read_stderr, daemon=True)
        self.reader.start(); self.errors.start()
        if initialize:
            self.initialized = self.request('initialize', {'protocolVersion': 1, 'locale': 'zh-Hans'})['result']

    def read_stdout(self):
        try:
            for line in self.proc.stdout:
                self.messages.put(json.loads(line))
        except Exception as error:
            self.messages.put(error)
        finally:
            self.messages.put(EOFError('Worker stdout closed'))

    def read_stderr(self):
        while data := self.proc.stderr.read(65536):
            self.stderr_count += len(data)

    def send(self, method, params=None, fragment=False):
        self.next_id += 1
        request_id = f'req-{self.next_id}'
        frame = (json.dumps({'jsonrpc': '2.0', 'id': request_id, 'method': method, 'params': params or {}}, ensure_ascii=False) + '\n').encode()
        if fragment:
            for point in (1, 2, 7, 31):
                self.proc.stdin.write(frame[:point]); self.proc.stdin.flush(); frame = frame[point:]
        self.proc.stdin.write(frame); self.proc.stdin.flush()
        return request_id

    def receive(self, request_id, timeout=20):
        deadline = time.monotonic() + timeout
        while request_id not in self.responses:
            message = self.messages.get(timeout=max(.01, deadline - time.monotonic()))
            if isinstance(message, Exception):
                raise message
            assert message.get('jsonrpc') == '2.0', f'Nonprotocol stdout: {message}'
            if 'method' in message:
                self.events.append(message)
            else:
                self.responses[message.get('id')] = message
        return self.responses.pop(request_id)

    def request(self, method, params=None, fragment=False):
        return self.receive(self.send(method, params, fragment))

    def operation(self, operation_id, timeout=20):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            status = self.request('operations.get', {'operationId': operation_id})['result']
            if status['status'] != 'running':
                return status
            time.sleep(.01)
        raise TimeoutError(f'Operation still running: {operation_id}')

    def import_video(self, name='DEMO-001.mp4', nfo=NFO):
        media = self.directory / '合成 片库' / name
        media.parent.mkdir(parents=True, exist_ok=True)
        media.write_bytes(b'SYNTHETIC VIDEO ONLY\0\1\2')
        if nfo is not None:
            media.with_suffix('.nfo').write_text(nfo)
        result = self.request('workspace.importInputs', {'paths': [str(media)], 'recursive': True})
        operation = self.operation(result['result']['operationId'])
        assert operation['status'] == 'completed', operation
        job_id = operation['result']['importedJobIds'][0]
        return media, self.request('jobs.get', {'jobId': job_id})['result']

    def preview(self, job, overwrite=True, **options):
        result = self.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'options': {'overwriteExisting': overwrite, 'includeIdInTitle': False, **options}})
        assert 'result' in result, result
        return result['result']

    def get_job(self, job_id):
        return self.request('jobs.get', {'jobId': job_id})['result']

    def import_paths(self, paths, recursive=True):
        started = self.request('workspace.importInputs', {'paths': [str(path) for path in paths], 'recursive': recursive})
        assert 'result' in started, started
        result = self.operation(started['result']['operationId'])
        assert result['status'] == 'completed', result
        return result['result']

    def discover_paths(self, paths, recursive=True):
        started = self.request('workspace.discoverInputs', {'paths': [str(path) for path in paths], 'recursive': recursive})
        assert 'result' in started, started
        done = self.operation(started['result']['operationId'])
        assert done['status'] == 'completed', done
        assert done['readOnly'] is True, done
        return done['result']

    def confirm_discovery(self, discovery, items):
        started = self.request('workspace.importDiscovery', {'discoveryId': discovery['discoveryId'], 'selectedItemIds': items})
        assert 'result' in started, started
        return self.operation(started['result']['operationId'])

    def edit(self, job, edits):
        response = self.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'edits': edits})
        assert 'result' in response, response
        return response['result']

    def close(self):
        if self.proc.poll() is None:
            try:
                self.request('shutdown')
                self.proc.stdin.close()
                self.proc.wait(timeout=20)
            except Exception:
                self.proc.kill(); self.proc.wait(timeout=5)
        self.reader.join(timeout=2); self.errors.join(timeout=2)
        for pipe in (self.proc.stdin, self.proc.stdout, self.proc.stderr):
            pipe.close()

    def __enter__(self): return self
    def __exit__(self, *args): self.close()


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite protocol 测试 ')
        self.addCleanup(self.temp.cleanup)

    def reason(self, response, expected):
        self.assertEqual(expected, response['error']['data']['reason'], response)

    def save_failure(self, worker, plan_id, expected):
        response = worker.request('save.execute', {'planId': plan_id})
        if 'error' in response:
            self.reason(response, expected)
            return response
        done = worker.operation(response['result']['operationId'])
        self.assertEqual('completed', done['status'], done)
        result = done['result']
        self.assertEqual('partialFailure', result['outcome'], done)
        self.assertEqual([], result['savedJobIds'], done)
        failed = [item for item in result['jobResults'] if item['status'] == 'Failed']
        self.assertEqual(1, len(failed), done)
        self.reason({'error': failed[0]['error']}, expected)
        return done

    def test_01_handshake_fragmented_and_back_to_back(self):
        with Worker(self.temp.name, initialize=False, stderr_bytes=2 * 1024 * 1024) as worker:
            self.reason(worker.request('workspace.getSnapshot'), 'NOT_INITIALIZED')
            hello = worker.request('initialize', {'protocolVersion': 1, 'futureOptional': {'ignored': True}}, fragment=True)['result']
            self.assertTrue(hello['capabilities']['writeNfo'])
            self.assertTrue(hello['capabilities']['writeArtwork'])
            self.assertTrue(hello['capabilities']['searchR18Dev'])
            self.assertTrue(hello['capabilities']['organizeVideo'])
            self.assertTrue(hello['capabilities']['batchSave'])
            ids = [worker.send('workspace.getSnapshot') for _ in range(25)]
            for request_id in reversed(ids):
                self.assertEqual([], worker.receive(request_id)['result']['jobs'])
            self.assertGreaterEqual(worker.stderr_count, 2 * 1024 * 1024)
            self.reason(worker.request('initialize', {'protocolVersion': 99}), 'PROTOCOL_VERSION_MISMATCH')
            self.reason(worker.request('workspace.getSnapshot'), 'NOT_INITIALIZED')
            worker.request('initialize', {'protocolVersion': 1})

    def test_02_parse_invalid_method_and_oversized_frame_recovery(self):
        with Worker(self.temp.name) as worker:
            worker.proc.stdin.write(b'{bad json}\n'); worker.proc.stdin.flush()
            error = worker.receive(None)
            self.assertEqual(-32700, error['error']['code'])
            self.assertIn('id', error)
            self.reason(worker.request('unknown.command'), 'METHOD_NOT_FOUND')
            worker.proc.stdin.write(b'{"jsonrpc":"2.0","id":42,"method":"workspace.getSnapshot"}\n'); worker.proc.stdin.flush()
            self.assertEqual(-32600, worker.receive(None)['error']['code'])
            worker.proc.stdin.write(b'x' * (8 * 1024 * 1024 + 1) + b'\n'); worker.proc.stdin.flush()
            self.reason(worker.receive(None), 'MESSAGE_TOO_LARGE')
            self.assertEqual([], worker.request('workspace.getSnapshot')['result']['jobs'])

    def test_03_import_edit_preview_save_roundtrip_and_duplicate_execute(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            before = hashlib.sha256(video.read_bytes()).hexdigest()
            self.assertEqual('2026 年 春', job['metadata']['releaseDate'])
            self.assertEqual('https://example.invalid/actor.jpg', job['metadata']['actors'][0]['imageUrl'])
            original_actor = job['metadata']['actors']
            job = worker.edit(job, {'title': '审核后的片名\nSecond line', 'rating': ''})
            self.assertEqual('元の題名', job['metadata']['originalTitle'])
            self.assertEqual(original_actor, job['metadata']['actors'])
            preview = worker.preview(job)
            self.assertTrue(preview['canExecute'], preview)
            self.assertTrue(any(change['target'].endswith('.nfo') for change in preview['changes']))
            execute = worker.request('save.execute', {'planId': preview['planId']})['result']
            duplicate = worker.request('save.execute', {'planId': preview['planId']})['result']
            self.assertEqual(execute['operationId'], duplicate['operationId'])
            done = worker.operation(execute['operationId'])
            self.assertEqual('completed', done['status'], done)
            repeated = worker.request('save.execute', {'planId': preview['planId']})['result']
            self.assertEqual(execute['operationId'], repeated['operationId'])
            xml = ET.parse(video.with_suffix('.nfo')).getroot()
            self.assertEqual('keep-me', xml.attrib['customAttribute'])
            self.assertEqual('KEEP UNKNOWN XML', xml.findtext('customNode/nested'))
            self.assertEqual('keep', xml.findtext('actor/customActor'))
            self.assertEqual('演示', xml.findtext('actor/role'))
            self.assertEqual('2026 年 春', xml.findtext('premiered'))
            self.assertEqual('約 90 分', xml.findtext('runtime'))
            self.assertEqual('审核后的片名\nSecond line', xml.findtext('title'))
            self.assertEqual(before, hashlib.sha256(video.read_bytes()).hexdigest())
            self.assertEqual('Completed', worker.request('jobs.get', {'jobId': job['jobId']})['result']['saveState'])
            journals = list((worker.data / 'recovery').glob('*.json'))
            self.assertEqual(1, len(journals), 'Duplicate submission started another transaction')
            self.assertEqual('completed', json.loads(journals[0].read_text())['status'])
            sequences = [event['params']['sequence'] for event in worker.events if event['params'].get('operationId') == execute['operationId']]
            self.assertEqual(sorted(set(sequences)), sequences)

    def test_04_stale_revision_and_plan_preserve_content(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            original = video.with_suffix('.nfo').read_bytes()
            preview = worker.preview(job)
            current = worker.request('jobs.get', {'jobId': job['jobId']})['result']
            edited = worker.edit(current, {'plot': 'New plot'})
            self.reason(worker.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': current['reviewRevision'], 'edits': {'plot': 'Stale text'}}), 'REVISION_CONFLICT')
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PLAN_STALE')
            self.assertEqual(original, video.with_suffix('.nfo').read_bytes())
            invalid = worker.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': edited['reviewRevision'], 'edits': {'title': 'Must not partly apply', 'unknownField': 'x'}})
            self.reason(invalid, 'INVALID_PARAMS')
            self.assertEqual(edited['metadata']['title'], worker.request('jobs.get', {'jobId': job['jobId']})['result']['metadata']['title'])

    def test_05_external_nfo_changes_and_target_appearance(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            preview = worker.preview(job)
            external = '<movie><title>External replacement</title></movie>'
            video.with_suffix('.nfo').write_text(external)
            self.save_failure(worker, preview['planId'], 'SOURCE_CHANGED')
            self.assertEqual(external, video.with_suffix('.nfo').read_text())
            new_video, new_job = worker.import_video('DEMO-002.mp4', None)
            new_job = worker.edit(new_job, {'title': 'New metadata'})
            new_preview = worker.preview(new_job, overwrite=True)
            new_video.with_suffix('.nfo').write_text(external)
            self.save_failure(worker, new_preview['planId'], 'SOURCE_CHANGED')
            self.assertEqual(external, new_video.with_suffix('.nfo').read_text())

    def test_06_nfo_security_and_explicit_overwrite(self):
        with Worker(self.temp.name) as worker:
            for number, nfo in enumerate(['<movie><title>broken', '<!DOCTYPE movie [<!ENTITY x SYSTEM "file:///etc/passwd">]><movie><title>&x;</title></movie>'], 1):
                video, job = worker.import_video(f'DEMO-10{number}.mp4', nfo)
                self.assertTrue(job['localNfoSaveBlocked'])
                response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision']})
                self.reason(response, 'SOURCE_CHANGED')
                self.assertEqual(nfo, video.with_suffix('.nfo').read_text())
            video, job = worker.import_video()
            job = worker.edit(job, {'title': 'Requires explicit overwrite'})
            preview = worker.preview(job, overwrite=False)
            self.assertFalse(preview['canExecute'])
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PATH_CONFLICT')

    def test_07_duplicate_import_remove_and_partial_success(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            imported = worker.request('workspace.importInputs', {'paths': [str(video), str(video.parent / 'missing')], 'recursive': True})['result']
            result = worker.operation(imported['operationId'])['result']
            self.assertEqual([job['jobId']], result['duplicateJobIds'])
            self.assertTrue(result['warnings'])
            self.assertEqual(1, len(worker.request('workspace.getSnapshot')['result']['jobs']))
            self.assertTrue(worker.request('jobs.remove', {'jobId': job['jobId']})['result']['removed'])
            self.assertTrue(video.exists()); self.assertTrue(video.with_suffix('.nfo').exists())
            self.assertEqual([], worker.request('workspace.getSnapshot')['result']['jobs'])

    def test_08_restart_invalidates_plan_and_preserves_unresolved_journal(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            plan = worker.preview(job)['planId']
        data = Path(self.temp.name) / 'engine data 数据'
        journal = data / 'recovery' / 'synthetic-unconfirmed.json'
        journal.write_text(json.dumps({'status': 'inProgress', 'transactionId': 'synthetic', 'planId': plan}))
        with Worker(self.temp.name) as worker:
            self.reason(worker.request('save.execute', {'planId': plan}), 'PLAN_STALE')
            snapshot = worker.request('workspace.getSnapshot')['result']
            self.assertIn(str(journal), snapshot['recoveryWarnings'])
            _, job = worker.import_video('DEMO-002.mp4', None)
            self.reason(worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision']}), 'RECOVERY_REQUIRED')
            self.assertTrue(journal.exists())

    def test_09_eof_during_save_exits_without_replay_or_orphan(self):
        worker = Worker(self.temp.name)
        self.addCleanup(worker.close)
        video, job = worker.import_video()
        job = worker.edit(job, {'title': 'EOF transaction'})
        preview = worker.preview(job)
        worker.request('save.execute', {'planId': preview['planId']})
        worker.proc.stdin.close()
        self.assertEqual(0, worker.proc.wait(timeout=20))
        self.assertEqual(b'SYNTHETIC VIDEO ONLY\0\1\2', video.read_bytes())
        xml = ET.parse(video.with_suffix('.nfo')).getroot()
        self.assertIn(xml.findtext('title'), ['EOF transaction', '合成影片 · Synthetic Film'])
        self.assertEqual('KEEP UNKNOWN XML', xml.findtext('customNode/nested'))
        for path in (worker.data / 'recovery').glob('*.json'):
            self.assertIn(json.loads(path.read_text())['status'], ['completed', 'canceledRestored'])

    def test_10_shared_cross_language_golden_fixture(self):
        golden = json.loads((ROOT / 'contracts/worker-v1/golden.json').read_text())
        metadata = golden['metadata']
        root = ET.Element('movie')
        for tag, field in [('id', 'id'), ('title', 'title'), ('originaltitle', 'originalTitle'), ('premiered', 'releaseDate'), ('runtime', 'runtimeMinutes'), ('rating', 'rating'), ('plot', 'plot')]:
            ET.SubElement(root, tag).text = metadata[field]
        for person in metadata['actors']:
            actor = ET.SubElement(root, 'actor')
            ET.SubElement(actor, 'name').text = person['name']
            ET.SubElement(actor, 'thumb').text = person['imageUrl']
        with Worker(self.temp.name, initialize=False) as worker:
            worker.proc.stdin.write((json.dumps(golden['initialize'], ensure_ascii=False) + '\n').encode()); worker.proc.stdin.flush()
            self.assertEqual(1, worker.receive('golden-1')['result']['protocolVersion'])
            _, job = worker.import_video(nfo=ET.tostring(root, encoding='unicode'))
            self.assertTrue(set(metadata).issubset(job['metadata']))
            self.assertEqual(metadata['actors'], job['metadata']['actors'])
            self.assertEqual(metadata['runtimeMinutes'], job['metadata']['runtimeMinutes'])
            edits = {k: v for k, v in golden['edit']['params']['edits'].items() if k != 'actorsText'}
            changed = worker.edit(job, edits)
            for key, expected in edits.items():
                self.assertEqual(expected, changed['metadata'][key])
            self.assertEqual(metadata['actors'], changed['metadata']['actors'])


    def test_11_new_nfo_and_repeat_edit_save_cycle(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video('DEMO-201.mp4', None)
            for title in ['First new NFO', 'Second edited value']:
                job = worker.edit(job, {'title': title})
                preview = worker.preview(job, overwrite=True)
                self.assertTrue(preview['canExecute'])
                operation_id = worker.request('save.execute', {'planId': preview['planId']})['result']['operationId']
                self.assertEqual('completed', worker.operation(operation_id)['status'])
                self.assertEqual(title, ET.parse(video.with_suffix('.nfo')).getroot().findtext('title'))
                job = worker.request('jobs.get', {'jobId': job['jobId']})['result']
                self.assertFalse(job['hasUnsavedChanges'])
                self.assertEqual(title, job['metadata']['title'])
            self.assertEqual(b'SYNTHETIC VIDEO ONLY\0\1\2', video.read_bytes())


    def test_12_rejected_large_edit_is_atomic_and_bad_types_are_protocol_errors(self):
        with Worker(self.temp.name) as worker:
            _, job = worker.import_video()
            response = worker.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'edits': {'plot': 'x' * (1100 * 1024)}})
            self.assertIn('error', response)
            current = worker.request('jobs.get', {'jobId': job['jobId']})['result']
            self.assertEqual(job['reviewRevision'], current['reviewRevision'])
            self.assertEqual(job['metadata'], current['metadata'])
            self.assertEqual(1, len(worker.request('workspace.getSnapshot')['result']['jobs']))
            worker.proc.stdin.write(b'{"jsonrpc":2,"id":"bad-type","method":"workspace.getSnapshot"}\n'); worker.proc.stdin.flush()
            self.assertEqual(-32600, worker.receive(None)['error']['code'])
            malformed = worker.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision']})
            self.assertEqual(-32602, malformed['error']['code'])


    def make_media(self, relative, movie_id=None, nfo=True, artwork=False):
        video = Path(self.temp.name) / relative
        video.parent.mkdir(parents=True, exist_ok=True)
        video.write_bytes(b'SYNTHETIC M4 VIDEO\x00' + str(relative).encode())
        if nfo:
            xml = ET.fromstring(NFO)
            if movie_id:
                xml.find('id').text = movie_id
            video.with_suffix('.nfo').write_text(ET.tostring(xml, encoding='unicode'))
        if artwork:
            video.with_name(video.stem + '-poster.png').write_bytes((ROOT / 'fixtures/macos/synthetic-poster-2x3.png').read_bytes())
            video.with_name(video.stem + '-fanart.png').write_bytes((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes())
        return video

    def batch_preview(self, worker, jobs, **options):
        response = worker.request('save.preview', {
            'jobIds': [job['jobId'] for job in jobs],
            'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in jobs},
            'options': {'overwriteExisting': True, 'includeIdInTitle': False, **options}})
        self.assertIn('result', response, response)
        return response['result']

    def execute(self, worker, preview):
        self.assertTrue(preview['canExecute'], preview)
        response = worker.request('save.execute', {'planId': preview['planId']})
        self.assertIn('result', response, response)
        return worker.operation(response['result']['operationId'])

    def test_13_organization_modes_preserve_video_and_unknown_nfo(self):
        for mode in ['VideoDirectory', 'SourceNumberFolder', 'CustomRootNumberFolder']:
            with self.subTest(mode=mode), Worker(Path(self.temp.name) / mode) as worker:
                video = self.make_media(f'{mode}/片库 空格/source-original.mp4', 'DEMO-301')
                before = hashlib.sha256(video.read_bytes()).hexdigest()
                imported = worker.import_paths([video])
                job = worker.get_job(imported['importedJobIds'][0])
                job = worker.edit(job, {'title': 'Organized safely'})
                custom = Path(self.temp.name) / mode / '自定义 目标'
                custom.mkdir()
                preview = worker.preview(job, targetMode=mode, renameVideo=mode != 'VideoDirectory', customRootDirectory=str(custom))
                if mode != 'VideoDirectory':
                    self.assertTrue(any(change['operation'] == 'CopyAndVerifyVideo' for change in preview['changes']), preview)
                done = self.execute(worker, preview)
                self.assertEqual('completed', done['status'], done)
                self.assertEqual('completed', done['result']['outcome'], done)
                current = worker.get_job(job['jobId'])
                destination = Path(current['videoPaths'][0])
                self.assertTrue(destination.is_file())
                self.assertEqual(before, hashlib.sha256(destination.read_bytes()).hexdigest())
                if mode == 'VideoDirectory':
                    self.assertEqual(video.resolve(), destination.resolve())
                else:
                    parent = custom if mode == 'CustomRootNumberFolder' else video.parent
                    self.assertEqual((parent / 'DEMO-301').resolve(), destination.parent.resolve())
                    self.assertEqual('DEMO-301.mp4', destination.name)
                    self.assertFalse(video.exists())
                nfo = Path(current['localNfoPath'])
                xml = ET.parse(nfo).getroot()
                self.assertEqual('KEEP UNKNOWN XML', xml.findtext('customNode/nested'))
                self.assertEqual('Organized safely', xml.findtext('title'))
                self.assertFalse(current['hasUnsavedChanges'])

    def test_14_organization_never_overwrites_video_and_missing_root_is_not_recreated(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('input/DEMO-302.mp4', 'DEMO-302')
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            custom = Path(self.temp.name) / 'custom-root'
            target = custom / 'DEMO-302' / 'DEMO-302.mp4'
            target.parent.mkdir(parents=True)
            target.write_bytes(b'EXISTING DIFFERENT VIDEO')
            before = video.read_bytes()
            preview = worker.preview(job, targetMode='CustomRootNumberFolder', renameVideo=True, customRootDirectory=str(custom))
            self.assertFalse(preview['canExecute'], preview)
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PATH_CONFLICT')
            self.assertEqual(b'EXISTING DIFFERENT VIDEO', target.read_bytes())
            self.assertEqual(before, video.read_bytes())
            target.unlink(); target.parent.rmdir()
            current = worker.get_job(job['jobId'])
            preview = worker.preview(current, targetMode='CustomRootNumberFolder', renameVideo=True, customRootDirectory=str(custom))
            self.assertTrue(preview['canExecute'], preview)
            custom.rmdir()
            response = worker.request('save.execute', {'planId': preview['planId']})
            if 'result' in response:
                done = worker.operation(response['result']['operationId'])
                self.assertNotIn(job['jobId'], (done.get('result') or {}).get('savedJobIds', []), done)
            else:
                self.assertIn(response['error']['data']['reason'], ['SOURCE_CHANGED', 'VOLUME_UNAVAILABLE', 'PATH_CONFLICT'])
            self.assertFalse(custom.exists(), 'An unavailable destination must not be silently recreated')
            self.assertEqual(before, video.read_bytes())

    def test_15_moving_video_full_hash_detects_same_size_same_timestamp_edit(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('original/DEMO-303.mp4', 'DEMO-303')
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            preview = worker.preview(job, targetMode='SourceNumberFolder', renameVideo=True)
            stamp = video.stat()
            changed = bytes([video.read_bytes()[0] ^ 0x01]) + video.read_bytes()[1:]
            video.write_bytes(changed)
            os.utime(video, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
            self.assertEqual(stamp.st_size, video.stat().st_size)
            self.assertEqual(stamp.st_mtime_ns, video.stat().st_mtime_ns)
            self.save_failure(worker, preview['planId'], 'SOURCE_CHANGED')
            self.assertEqual(changed, video.read_bytes())
            self.assertFalse((video.parent / 'DEMO-303').exists())

    def test_16_batch_executes_in_reviewed_order_and_is_idempotent(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'batch/item-{index}/DEMO-{310+index}.mp4', f'DEMO-{310+index}') for index in range(3)]
            imported = worker.import_paths(videos)
            jobs = [worker.edit(worker.get_job(job_id), {'title': 'Batch approved ' + str(index)}) for index, job_id in enumerate(imported['importedJobIds'])]
            jobs.reverse()
            expected = [job['jobId'] for job in jobs]
            preview = self.batch_preview(worker, jobs)
            self.assertEqual(expected, [job['jobId'] for job in preview['jobs']])
            started = worker.request('save.execute', {'planId': preview['planId']})['result']
            self.assertEqual(started, worker.request('save.execute', {'planId': preview['planId']})['result'])
            done = worker.operation(started['operationId'])
            self.assertEqual('completed', done['status'], done)
            self.assertEqual('completed', done['result']['outcome'], done)
            self.assertEqual(expected, done['result']['savedJobIds'])
            self.assertEqual([], done['result']['notStartedJobIds'])
            self.assertEqual(expected, [item['jobId'] for item in done['result']['jobResults']])
            self.assertTrue(all(item['status'] == 'Completed' for item in done['result']['jobResults']))
            for job in jobs:
                current = worker.get_job(job['jobId'])
                self.assertEqual(job['metadata']['title'], ET.parse(current['localNfoPath']).getroot().findtext('title'))
                self.assertFalse(current['hasUnsavedChanges'])
            journals = list((worker.data / 'recovery').glob('*.json'))
            self.assertEqual(3, len(journals))

    def test_17_batch_aggregate_collision_and_skipped_input_protection(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'collision/{name}/original.mp4', 'DEMO-320') for name in ['one', 'two']]
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths(videos)['importedJobIds']]
            custom = Path(self.temp.name) / 'aggregate output'
            custom.mkdir()
            preview = self.batch_preview(worker, jobs, targetMode='CustomRootNumberFolder', renameVideo=True, customRootDirectory=str(custom))
            self.assertFalse(preview['canExecute'], preview)
            self.assertTrue(preview['blockingConflicts'])
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PATH_CONFLICT')
            self.assertTrue(all(video.exists() for video in videos))
            self.assertEqual([], list(custom.iterdir()))
            # Another queued job is protected even when absent from the reviewed batch.
            protected = self.make_media('protected/DEMO-321/DEMO-321.mp4', 'DEMO-321')
            protected_nfo = protected.with_suffix('.nfo').read_bytes()
            worker.import_paths([protected])
            source = self.make_media('source/DEMO-321.mp4', 'DEMO-321')
            source_job = worker.get_job(worker.import_paths([source])['importedJobIds'][0])
            protected_plan = self.batch_preview(worker, [source_job], targetMode='CustomRootNumberFolder', renameVideo=True, customRootDirectory=str(protected.parent.parent))
            self.assertFalse(protected_plan['canExecute'], protected_plan)
            self.assertEqual(protected_nfo, protected.with_suffix('.nfo').read_bytes())

    def test_18_batch_stale_revision_or_external_sidecar_stops_before_any_write(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'stale/{index}/DEMO-{330+index}.mp4', f'DEMO-{330+index}') for index in range(2)]
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths(videos)['importedJobIds']]
            before = [video.with_suffix('.nfo').read_bytes() for video in videos]
            preview = self.batch_preview(worker, jobs)
            jobs[1] = worker.edit(worker.get_job(jobs[1]['jobId']), {'title': 'Updated after batch preview'})
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PLAN_STALE')
            self.assertEqual(before, [video.with_suffix('.nfo').read_bytes() for video in videos])
            jobs = [worker.get_job(job['jobId']) for job in jobs]
            preview = self.batch_preview(worker, jobs)
            external = b'<movie><title>Externally edited last batch item</title></movie>'
            videos[1].with_suffix('.nfo').write_bytes(external)
            self.save_failure(worker, preview['planId'], 'SOURCE_CHANGED')
            self.assertEqual(before[0], videos[0].with_suffix('.nfo').read_bytes())
            self.assertEqual(external, videos[1].with_suffix('.nfo').read_bytes())

    def test_19_recursive_multipart_and_reimport_after_move_keep_one_job(self):
        with Worker(self.temp.name) as worker:
            first = self.make_media('recursive/deep/DEMO-340-CD1.mp4', 'DEMO-340', nfo=False)
            second = self.make_media('recursive/deep/DEMO-340-CD2.mp4', 'DEMO-340', nfo=False)
            unrelated = self.make_media('recursive/other/DEMO-341.mp4', 'DEMO-341')
            first.with_suffix('.srt').write_text('synthetic subtitle')
            imported = worker.import_paths([Path(self.temp.name) / 'recursive'])
            self.assertEqual(2, len(imported['importedJobIds']), imported)
            jobs = [worker.get_job(job_id) for job_id in imported['importedJobIds']]
            multipart = next(job for job in jobs if len(job['videoPaths']) == 2)
            self.assertEqual([first.resolve(), second.resolve()], [Path(path).resolve() for path in multipart['videoPaths']])
            multipart = worker.edit(multipart, {'id': 'DEMO-340', 'title': 'Two-part synthetic film'})
            before = [hashlib.sha256(path.read_bytes()).hexdigest() for path in [first, second]]
            done = self.execute(worker, worker.preview(multipart, targetMode='SourceNumberFolder', renameVideo=True))
            self.assertEqual('completed', done['status'], done)
            current = worker.get_job(multipart['jobId'])
            self.assertEqual(before, [hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in current['videoPaths']])
            duplicate = worker.import_paths(current['videoPaths'])
            self.assertEqual([], duplicate['importedJobIds'])
            self.assertEqual([current['jobId']], duplicate['duplicateJobIds'])
            self.assertTrue(unrelated.exists())


    def test_20_local_artwork_and_samples_migrate_with_real_files(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('artwork/DEMO-350.mp4', 'DEMO-350', artwork=True)
            samples = video.parent / 'extrafanart'
            samples.mkdir()
            for index, fixture in enumerate(['synthetic-cover-5x2.png', 'synthetic-poster-2x3.png'], 1):
                (samples / f'fanart{index}.png').write_bytes((ROOT / 'fixtures/macos' / fixture).read_bytes())
            originals = {path.name: path.read_bytes() for path in video.parent.rglob('*.png')}
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            self.assertTrue(any(item['kind'] == 'poster' for item in job['artwork']), job)
            self.assertEqual(2, len([item for item in job['artwork'] if item['kind'] == 'sample']))
            cover = next(item for item in job['artwork'] if item['kind'] == 'fanart')
            preview = worker.request('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': cover['artworkId']})['result']
            self.assertEqual((5, 2), (preview['width'], preview['height']))
            cache = Path(preview['path'])
            self.assertTrue(cache.resolve().is_relative_to(Path(worker.initialized['cacheRoot']).resolve()))
            self.assertEqual(originals['DEMO-350-fanart.png'], cache.read_bytes())
            plan = worker.preview(job, targetMode='SourceNumberFolder', renameVideo=True, downloadPoster=True, downloadFanart=True, downloadExtrafanart=True)
            done = self.execute(worker, plan)
            self.assertEqual('completed', done['status'], done)
            self.assertEqual('completed', done['result']['outcome'], done)
            current = worker.get_job(job['jobId'])
            target_directory = Path(current['videoPaths'][0]).parent
            generated = list(target_directory.rglob('*.png'))
            self.assertEqual(4, len(generated), done)
            self.assertEqual(sorted(hashlib.sha256(value).hexdigest() for value in originals.values()), sorted(hashlib.sha256(path.read_bytes()).hexdigest() for path in generated))
            self.assertEqual(2, len([item for item in current['artwork'] if item['kind'] == 'sample']))
            self.assertGreaterEqual(len(done['result']['outputs']), 5, 'Result must report written NFO and images')
            self.assertTrue(all(Path(path).exists() for path in done['result']['outputs']))

    def test_21_artwork_selection_is_revisioned_and_does_not_write_before_save(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('selection/DEMO-351.mp4', 'DEMO-351', artwork=True)
            sample = video.parent / 'extrafanart' / 'fanart1.png'
            sample.parent.mkdir()
            sample.write_bytes((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes())
            before = {str(path): path.read_bytes() for path in video.parent.rglob('*') if path.is_file()}
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            plan = worker.preview(job)
            job = worker.get_job(job['jobId'])
            item = next(item for item in job['artwork'] if item['kind'] == 'sample')
            updated = worker.request('artwork.select', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'artworkId': item['artworkId'], 'selected': False})
            self.assertIn('result', updated, updated)
            changed = updated['result']
            self.assertGreater(changed['reviewRevision'], job['reviewRevision'])
            self.assertFalse(next(item for item in changed['artwork'] if item['kind'] == 'sample')['selected'])
            self.reason(worker.request('save.execute', {'planId': plan['planId']}), 'PLAN_STALE')
            self.assertEqual(before, {str(path): path.read_bytes() for path in video.parent.rglob('*') if path.is_file()})
            stale = worker.request('artwork.select', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'artworkId': item['artworkId'], 'selected': True})
            self.reason(stale, 'REVISION_CONFLICT')
            invalid = worker.request('jobs.search', {'jobId': job['jobId'], 'sourceMode': 'unsupported-provider'})
            self.reason(invalid, 'INVALID_PARAMS')
            self.assertEqual(changed['metadata'], worker.get_job(job['jobId'])['metadata'])

    def test_22_explicit_multipart_confirmation_has_one_transaction(self):
        with Worker(self.temp.name) as worker:
            first = self.make_media('confirm/segment-first.mp4', 'DEMO-360')
            second = self.make_media('confirm/segment-second.mp4', 'DEMO-360')
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths([first, second])['importedJobIds']]
            self.assertEqual(2, len(jobs))
            expected_bytes = [Path(job['videoPaths'][0]).read_bytes() for job in jobs]
            response = worker.request('save.preview', {'jobIds': [job['jobId'] for job in jobs],
                'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in jobs},
                'confirmedParts': True, 'options': {'overwriteExisting': True, 'includeIdInTitle': False, 'targetMode': 'SourceNumberFolder', 'renameVideo': True}})
            self.assertIn('result', response, response)
            done = self.execute(worker, response['result'])
            self.assertEqual('completed', done['status'], done)
            snapshot = worker.request('workspace.getSnapshot')['result']
            self.assertEqual(1, len(snapshot['jobs']))
            retained = snapshot['jobs'][0]
            self.assertEqual(jobs[0]['jobId'], retained['jobId'])
            self.assertEqual(expected_bytes, [Path(path).read_bytes() for path in retained['videoPaths']])
            self.assertEqual(1, len(list((worker.data / 'recovery').glob('*.json'))))


    def test_23_batch_cancel_retains_committed_predecessor_and_not_started_tail(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'cancel/{index}/DEMO-{370+index}.mp4', f'DEMO-{370+index}') for index in range(3)]
            first_bytes = videos[0].read_bytes()
            last_bytes = videos[2].read_bytes()
            second_prefix = videos[1].read_bytes()
            # Sparse synthetic payload makes the real verified-copy stage observable.
            # No production delay/failure hooks and no developer media are used.
            size = 512 * 1024 * 1024
            end_marker = b'SYNTHETIC FILE END MARKER'
            with videos[1].open('r+b') as stream:
                stream.truncate(size)
                stream.seek(size - len(end_marker))
                stream.write(end_marker)
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths(videos)['importedJobIds']]
            preview = self.batch_preview(worker, jobs, targetMode='SourceNumberFolder', renameVideo=True)
            self.assertTrue(preview['canExecute'], preview)
            operation_id = worker.request('save.execute', {'planId': preview['planId']})['result']['operationId']
            observed_copy = False
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline:
                status = worker.request('operations.get', {'operationId': operation_id})['result']
                for event in worker.events:
                    params = event.get('params', {})
                    if params.get('operationId') != operation_id:
                        continue
                    details = params.get('details') or {}
                    if (details.get('jobId') == jobs[1]['jobId'] and params.get('phase') in ['CopyingMovie', 'VerifyingMovie']):
                        observed_copy = True
                        break
                if observed_copy or status['status'] != 'running':
                    break
                time.sleep(.001)
            self.assertTrue(observed_copy, 'Expected an observable real second-item verified-copy stage')
            blocked_import = worker.request('workspace.importInputs', {'paths': [str(videos[2])], 'recursive': True})
            self.reason(blocked_import, 'JOB_BUSY')
            acknowledgment = worker.request('operations.cancel', {'operationId': operation_id})
            self.assertTrue(acknowledgment['result']['received'])
            done = worker.operation(operation_id)
            self.assertEqual('completed', done['status'], done)
            result = done['result']
            self.assertEqual('canceled', result['outcome'], done)
            self.assertEqual([jobs[0]['jobId']], result['savedJobIds'])
            self.assertEqual([jobs[2]['jobId']], result['notStartedJobIds'])
            self.assertEqual(['Completed', 'Canceled', 'NotStarted'], [item['status'] for item in result['jobResults']])
            saved = worker.get_job(jobs[0]['jobId'])
            self.assertEqual(first_bytes, Path(saved['videoPaths'][0]).read_bytes())
            self.assertFalse(videos[0].exists())
            self.assertTrue(videos[1].exists())
            self.assertEqual(size, videos[1].stat().st_size)
            with videos[1].open('rb') as stream:
                self.assertEqual(second_prefix, stream.read(len(second_prefix)))
                stream.seek(-len(end_marker), 2)
                self.assertEqual(end_marker, stream.read())
            self.assertEqual(last_bytes, videos[2].read_bytes())
            self.assertFalse((videos[1].parent / 'DEMO-371').exists())
            self.assertFalse((videos[2].parent / 'DEMO-372').exists())

    def test_24_async_preview_returns_owned_revisioned_plan(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('async/DEMO-380.mp4', 'DEMO-380')
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'async': True,
                'options': {'targetMode': 'SourceNumberFolder', 'renameVideo': True, 'overwriteExisting': True}})
            self.assertIn('operationId', response['result'], response)
            done = worker.operation(response['result']['operationId'])
            self.assertEqual('completed', done['status'], done)
            preview = done['result']
            self.assertTrue(preview['canExecute'], preview)
            self.assertTrue(preview['planId'])
            self.assertEqual(job['jobId'], preview['jobs'][0]['jobId'])
            current = worker.get_job(job['jobId'])
            worker.edit(current, {'title': 'Changed after asynchronous review'})
            self.reason(worker.request('save.execute', {'planId': preview['planId']}), 'PLAN_STALE')
            self.assertTrue(video.exists())


    def test_25_invalid_batch_preflight_is_atomic(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'atomic/{index}/DEMO-{390+index}.mp4', f'DEMO-{390+index}') for index in range(2)]
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths(videos)['importedJobIds']]
            before = [job['reviewRevision'] for job in jobs]
            invalid_revisions = {job['jobId']: job['reviewRevision'] for job in jobs}
            invalid_revisions[jobs[1]['jobId']] -= 1
            result = worker.request('save.preview', {'jobIds': [job['jobId'] for job in jobs], 'expectedReviewRevisions': invalid_revisions,
                'options': {'targetMode': 'SourceNumberFolder', 'renameVideo': True, 'overwriteExisting': True}})
            self.reason(result, 'REVISION_CONFLICT')
            self.assertEqual(before, [worker.get_job(job['jobId'])['reviewRevision'] for job in jobs])
            self.assertEqual([], list((worker.data / 'recovery').glob('*.json')))
            self.assertTrue(all(video.exists() for video in videos))
            duplicate = worker.request('save.preview', {'jobIds': [jobs[0]['jobId'], jobs[0]['jobId']],
                'expectedReviewRevisions': {jobs[0]['jobId']: jobs[0]['reviewRevision']}, 'options': {'overwriteExisting': True}})
            if 'error' in duplicate:
                self.reason(duplicate, 'INVALID_PARAMS')
            else:
                self.assertEqual(1, len(duplicate['result']['jobs']), 'Duplicate IDs must never schedule the movie twice')


    def test_26_batch_late_source_change_retains_first_and_preserves_external_bytes(self):
        with Worker(self.temp.name) as worker:
            videos = [self.make_media(f'latechange/{index}/DEMO-{400+index}.mp4', f'DEMO-{400+index}') for index in range(3)]
            first_bytes, last_bytes = videos[0].read_bytes(), videos[2].read_bytes()
            original_prefix = videos[1].read_bytes()
            with videos[1].open('r+b') as stream:
                stream.truncate(512 * 1024 * 1024)
            jobs = [worker.get_job(job_id) for job_id in worker.import_paths(videos)['importedJobIds']]
            plan = self.batch_preview(worker, jobs, targetMode='SourceNumberFolder', renameVideo=True)
            self.assertTrue(plan['canExecute'], plan)
            operation_id = worker.request('save.execute', {'planId': plan['planId']})['result']['operationId']
            observed_copy = False
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline:
                status = worker.request('operations.get', {'operationId': operation_id})['result']
                observed_copy = any(event.get('params', {}).get('operationId') == operation_id
                    and event.get('params', {}).get('details', {}).get('jobId') == jobs[1]['jobId']
                    and event.get('params', {}).get('phase') == 'CopyingMovie'
                    and event.get('params', {}).get('details', {}).get('completedBytes', 0) > 0 for event in worker.events)
                if observed_copy or status['status'] != 'running':
                    break
                time.sleep(.001)
            self.assertTrue(observed_copy, 'Expected real copy progress before the external edit')
            with videos[1].open('r+b') as stream:
                stream.write(b'X')
                stream.flush()
                os.fsync(stream.fileno())
            done = worker.operation(operation_id)
            self.assertEqual('completed', done['status'], done)
            result = done['result']
            self.assertIn(result['outcome'], ['partialFailure', 'recoveryRequired'], done)
            self.assertEqual([jobs[0]['jobId']], result['savedJobIds'])
            self.assertEqual([jobs[2]['jobId']], result['notStartedJobIds'])
            self.assertEqual(['Completed', 'Failed', 'NotStarted'], [item['status'] for item in result['jobResults']])
            self.assertEqual(first_bytes, Path(worker.get_job(jobs[0]['jobId'])['videoPaths'][0]).read_bytes())
            self.assertEqual(last_bytes, videos[2].read_bytes())
            self.assertTrue(videos[1].exists(), 'External source version must survive a failed copy transaction')
            with videos[1].open('rb') as stream:
                self.assertEqual(b'X' + original_prefix[1:], stream.read(len(original_prefix)))
            if result['outcome'] == 'recoveryRequired':
                snapshot = worker.request('workspace.getSnapshot')['result']
                self.assertTrue(snapshot['recoveryWarnings'])
                refreshed = worker.get_job(jobs[2]['jobId'])
                blocked = worker.request('save.preview', {'jobId': refreshed['jobId'], 'expectedReviewRevision': refreshed['reviewRevision']})
                self.reason(blocked, 'RECOVERY_REQUIRED')


    def test_27_organization_without_nfo_keeps_unsaved_metadata_draft(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('draft/DEMO-410.mp4', 'DEMO-410', artwork=True)
            original_nfo = video.with_suffix('.nfo').read_bytes()
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            job = worker.edit(job, {'title': 'Unsaved reviewed title', 'plot': 'Preserve draft after moving video', 'rating': '7 点 5', 'actorsText': 'Synthetic Actor'})
            reviewed = job['metadata']
            plan = worker.preview(job, writeNfo=False, downloadPoster=True, downloadFanart=True, targetMode='SourceNumberFolder', renameVideo=True)
            done = self.execute(worker, plan)
            self.assertEqual('completed', done['status'], done)
            self.assertEqual('completed', done['result']['outcome'], done)
            current = worker.get_job(job['jobId'])
            self.assertTrue(current['hasUnsavedChanges'], current)
            self.assertEqual(original_nfo, Path(current['localNfoPath']).read_bytes())
            for field in ['title', 'plot', 'rating', 'actors', 'originalTitle', 'releaseDate', 'runtimeMinutes']:
                self.assertEqual(reviewed[field], current['metadata'][field], field)
            self.assertFalse(video.exists())
            self.assertTrue(Path(current['videoPaths'][0]).exists())


    def test_28_local_samples_keep_valid_selection_without_nfo_write(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('regenerated/DEMO-411.mp4', 'DEMO-411')
            sample = video.parent / 'extrafanart' / 'custom-name.png'
            sample.parent.mkdir()
            sample.write_bytes((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes())
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            job = worker.edit(job, {'plot': 'Still unsaved after sample regeneration'})
            plan = worker.preview(job, writeNfo=False, downloadExtrafanart=True, replaceLocalExtrafanart=True, targetMode='SourceNumberFolder', renameVideo=True)
            done = self.execute(worker, plan)
            self.assertEqual('completed', done['status'], done)
            self.assertEqual('completed', done['result']['outcome'], done)
            current = worker.get_job(job['jobId'])
            self.assertTrue(current['hasUnsavedChanges'])
            self.assertEqual(job['metadata']['plot'], current['metadata']['plot'])
            selected = current['metadata']['screenshotUrls']
            self.assertEqual(1, len(selected), current)
            self.assertTrue(Path(selected[0]).is_file(), 'Selected local image must point at its actual committed location')
            self.assertEqual('custom-name.png', Path(selected[0]).name)
            self.assertEqual((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes(), Path(selected[0]).read_bytes())
            next_preview = worker.preview(current, writeNfo=False, downloadExtrafanart=True, replaceLocalExtrafanart=True)
            self.assertTrue(next_preview['canExecute'], next_preview)

    def test_67_artwork_content_identity_survives_selection_but_changes_for_replacement(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video()
            poster = video.parent / 'poster.png'; poster.write_bytes(synthetic_png(30, 40))
            samples = video.parent / 'extrafanart'; samples.mkdir(); (samples / 'fanart1.png').write_bytes(synthetic_png(40, 30))
            # Import a fresh job after discovering its complete sidecar set.
            worker.request('jobs.remove', {'jobId': job['jobId']})
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            old_keys = {a['artworkId']: a['contentKey'] for a in job['artwork']}
            sample = next(a for a in job['artwork'] if a['kind'] == 'sample')
            changed = worker.request('artwork.select', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'artworkId': sample['artworkId'], 'selected': not sample['selected']})['result']
            self.assertGreater(changed['reviewRevision'], job['reviewRevision'])
            self.assertEqual(old_keys, {a['artworkId']: a['contentKey'] for a in changed['artwork']})
            changed = worker.edit(changed, {'title': 'Metadata edit does not invalidate pictures'})
            self.assertEqual(old_keys, {a['artworkId']: a['contentKey'] for a in changed['artwork']})
            poster.write_bytes(synthetic_png(31, 40))
            external = worker.get_job(job['jobId'])
            new_keys = {a['artworkId']: a['contentKey'] for a in external['artwork']}
            self.assertNotEqual(old_keys['local-poster'], new_keys['local-poster'])
            self.assertEqual(old_keys[sample['artworkId']], new_keys[sample['artworkId']])

    def browser_import(self, worker, job, html=None, **changes):
        params = {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': 'javlibrary',
                  'url': 'https://www.javlibrary.com/cn/?v=synthetic',
                  'html': html if html is not None else (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text()}
        params.update(changes)
        response = worker.request('browser.importPage', params)
        if 'error' in response:
            return response
        return worker.operation(response['result']['operationId'])

    def prepare_trash(self, worker, job, artwork_id):
        response = worker.request('artwork.prepareTrash', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'artworkId': artwork_id})
        self.assertIn('result', response, response)
        return response['result']

    def test_29_browser_import_real_captured_html_roundtrip(self):
        with Worker(self.temp.name) as worker:
            self.assertTrue(worker.initialized['capabilities']['manualBrowserImport'])
            self.assertTrue(worker.initialized['capabilities']['localArtworkTrash'])
            video, job = worker.import_video()
            original = video.with_suffix('.nfo').read_bytes()
            job = worker.edit(job, {'title': 'Earlier manual browser review'})
            plan = worker.preview(job)
            job = worker.get_job(job['jobId'])
            targets = worker.request('browser.getTargets', {'jobId': job['jobId']})['result']
            self.assertEqual({'libredmm', 'r18dev', 'javlibrary'}, {t['source'] for t in targets['targets']})
            self.assertEqual(2097152, targets['maximumHtmlBytes'])
            self.assertTrue(worker.request('browser.getSupport', {'source': 'javlibrary', 'url': 'https://www.javlibrary.com/cn/?v=synthetic'})['result']['supported'])
            canonical = 'https://www.javlibrary.com/cn/javli43n4q.html'
            self.assertTrue(worker.request('browser.getSupport', {'source': 'javlibrary', 'url': canonical})['result']['supported'])
            done = self.browser_import(worker, job, url=canonical)
            self.assertEqual('completed', done['status'], done)
            current = done['result']['job']
            self.assertFalse(current['busy'])
            self.assertGreater(done['result']['promotedFieldCount'], 0)
            self.assertEqual('Synthetic browser title', current['metadata']['title'])
            self.assertGreater(current['reviewRevision'], job['reviewRevision'])
            self.assertTrue(any(c['value'] == 'Earlier manual browser review' for c in current['candidates']))
            self.assertEqual(original, video.with_suffix('.nfo').read_bytes(), 'Browser import must not save files')
            self.reason(worker.request('save.execute', {'planId': plan['planId']}), 'PLAN_STALE')
            self.reason(self.browser_import(worker, job), 'REVISION_CONFLICT')
            saved = self.execute(worker, worker.preview(current))
            self.assertEqual('completed', saved['result']['outcome'], saved)
            xml = ET.parse(video.with_suffix('.nfo')).getroot()
            self.assertEqual('KEEP UNKNOWN XML', xml.find('customNode/nested').text)
            self.assertEqual('Synthetic browser title', xml.find('title').text)
            self.assertEqual(b'SYNTHETIC VIDEO ONLY\0\1\2', video.read_bytes())

    def test_30_browser_guards_are_atomic_and_offline(self):
        with Worker(self.temp.name) as worker:
            _, job = worker.import_video()
            html = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text()
            for changes, reason in [
                ({'url': 'file:///tmp/local.html'}, 'PAGE_UNSUPPORTED'),
                ({'url': 'https://www.javlibrary.com.evil.invalid/?v=x'}, 'PAGE_UNSUPPORTED'),
                ({'url': 'https://name:password@www.javlibrary.com/?v=x'}, 'PAGE_UNSUPPORTED'),
                ({'source': 'libredmm'}, 'PAGE_UNSUPPORTED'),
                ({'html': '界' * 699051}, 'PAGE_TOO_LARGE'),
                ({'html': '<title>Just a moment</title>'}, 'PAGE_NOT_READY'),
                ({'source': 'r18dev', 'url': 'https://r18.dev/videos/vod/movies/detail/-/id=demoactual001/', 'html': '<title>Just a moment</title>'}, 'PAGE_NOT_READY'),
                ({'html': html.replace('DEMO-001', 'DEMO-999')}, 'PAGE_ID_MISMATCH'),
                ({'html': html.replace('<span class="text">DEMO-001</span>', '<span class="text"></span>')}, 'PAGE_ID_MISMATCH'),
            ]:
                result = self.browser_import(worker, job, **changes)
                self.reason(result if 'status' not in result else {'error': result['error']}, reason)
                current = worker.get_job(job['jobId'])
                self.assertFalse(current['busy'])
                self.assertEqual(job['reviewRevision'], current['reviewRevision'])
                self.assertEqual(job['metadata'], current['metadata'])
            unsupported = worker.request('browser.getSupport', {'source': 'r18dev', 'url': 'https://r18.dev/'})['result']
            self.assertFalse(unsupported['supported'])
            for method, params in [('browser.importPage', {'html': 5}), ('browser.getSupport', {'source': [], 'url': 'https://r18.dev/'})]:
                self.assertEqual(-32602, worker.request(method, params)['error']['code'])

    def test_31_shared_trash_reservation_refreshes_all_jobs_and_blocks_mutation(self):
        with Worker(self.temp.name) as worker:
            first = self.make_media('shared-trash/DEMO-001.mp4', 'DEMO-001')
            second = self.make_media('shared-trash/DEMO-002.mp4', 'DEMO-002')
            sample = first.parent / 'extrafanart' / 'shared.png'
            sample.parent.mkdir()
            sample.write_bytes((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes())
            other = self.make_media('other-trash/DEMO-003.mp4', 'DEMO-003')
            jobs = [worker.get_job(j) for j in worker.import_paths([first, second, other])['importedJobIds']]
            by_id = {j['metadata']['id']: j for j in jobs}
            one = worker.edit(by_id['DEMO-001'], {'title': 'Retain first unsaved manual title'})
            two = worker.edit(by_id['DEMO-002'], {'plot': 'Retain second unsaved plot'})
            third = by_id['DEMO-003']
            originals = {p: p.with_suffix('.nfo').read_bytes() for p in [first, second, other]}
            old_plan = worker.preview(one)
            other_plan = worker.preview(third)
            one = worker.get_job(one['jobId']); third = worker.get_job(third['jobId'])
            lease = self.prepare_trash(worker, one, 'local-sample-0')
            self.assertEqual([str(sample.resolve())], lease['paths'])
            self.assertEqual({one['jobId'], two['jobId']}, set(lease['affectedJobIds']))
            for method, params in [
                ('jobs.applyEdits', {'jobId': third['jobId'], 'expectedReviewRevision': third['reviewRevision'], 'edits': {'title': 'must block'}}),
                ('jobs.remove', {'jobId': third['jobId']}),
                ('workspace.importInputs', {'paths': [str(other)]}),
                ('save.execute', {'planId': other_plan['planId']}),
                ('save.preview', {'jobId': third['jobId'], 'expectedReviewRevision': third['reviewRevision']}),
                ('jobs.search', {'jobId': third['jobId']}),
            ]:
                self.reason(worker.request(method, params), 'TRASH_PENDING')
            state = worker.request('workspace.getSnapshot')['result']
            self.assertEqual('prepared', state['activeTrash']['phase'])
            token = {'trashToken': lease['trashToken']}
            self.assertTrue(worker.request('artwork.validateTrash', token)['result']['authorized'])
            self.reason(worker.request('artwork.abortTrash', token), 'TRASH_PHASE')
            self.assertEqual('authorized', worker.request('workspace.getSnapshot')['result']['activeTrash']['phase'])
            # Simulate the external native recycle result. This does not claim NSWorkspace or Trash validation.
            sample.rename(Path(self.temp.name) / 'simulated-trash.png')
            completed = worker.request('artwork.completeTrash', token)['result']
            self.assertEqual([str(sample.resolve())], completed['removedPaths'])
            self.assertEqual(2, len(completed['jobs']))
            for before in [one, two]:
                after = worker.get_job(before['jobId'])
                self.assertFalse(after['busy'])
                self.assertGreater(after['reviewRevision'], before['reviewRevision'])
                self.assertFalse(any(a['source'] == 'local' and a['kind'] == 'sample' for a in after['artwork']))
                for field in ['title', 'plot', 'actors', 'actorsText', 'originalTitle']:
                    self.assertEqual(before['metadata'].get(field), after['metadata'].get(field), field)
                self.assertEqual(before['candidates'], after['candidates'])
            self.reason(worker.request('save.execute', {'planId': old_plan['planId']}), 'PLAN_STALE')
            self.assertNotIn('activeTrash', worker.request('workspace.getSnapshot')['result'])
            for path, original in originals.items():
                self.assertEqual(original, path.with_suffix('.nfo').read_bytes())
                self.assertTrue(path.is_file())
            # Preserved unknown XML still round-trips after the separate artwork deletion.
            saved = self.execute(worker, worker.preview(worker.get_job(one['jobId'])))
            self.assertEqual('completed', saved['result']['outcome'])
            self.assertEqual('KEEP UNKNOWN XML', ET.parse(first.with_suffix('.nfo')).getroot().find('customNode/nested').text)

    def test_32_trash_changed_file_and_failed_recycle_release_with_truthful_refresh(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('changed-trash/DEMO-001.mp4', 'DEMO-001', artwork=True)
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            lease = self.prepare_trash(worker, job, 'local-poster')
            token = {'trashToken': lease['trashToken']}
            poster = Path(lease['paths'][0]); poster.write_bytes((ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes())
            self.reason(worker.request('artwork.validateTrash', token), 'SOURCE_CHANGED')
            self.reason(worker.request('artwork.abortTrash', token), 'SOURCE_CHANGED')
            refreshed = worker.request('artwork.completeTrash', token)['result']
            self.assertEqual([], refreshed['removedPaths'])
            self.assertTrue(refreshed['warnings'])
            self.assertTrue(poster.is_file())
            self.assertNotIn('activeTrash', worker.request('workspace.getSnapshot')['result'])
            current = worker.get_job(job['jobId'])
            lease = self.prepare_trash(worker, current, 'local-poster'); token = {'trashToken': lease['trashToken']}
            worker.request('artwork.validateTrash', token)
            # Native recycle reports failure: no filesystem change, then finalize verifies it.
            failed = worker.request('artwork.completeTrash', token)['result']
            self.assertEqual([], failed['removedPaths']); self.assertTrue(failed['warnings'])
            self.assertTrue(poster.is_file()); self.assertFalse(worker.get_job(job['jobId'])['busy'])
            self.reason(worker.request('artwork.completeTrash', token), 'TRASH_NOT_FOUND')
            lease = self.prepare_trash(worker, worker.get_job(job['jobId']), 'local-poster')
            self.assertTrue(worker.request('artwork.abortTrash', {'trashToken': lease['trashToken']})['result']['aborted'])
            lease = self.prepare_trash(worker, worker.get_job(job['jobId']), 'local-poster'); token = {'trashToken': lease['trashToken']}
            worker.request('artwork.validateTrash', token)
            video.parent.rename(video.parent.with_name('unavailable-parent'))
            unknown = worker.request('artwork.completeTrash', token)['result']
            self.assertEqual([], unknown['removedPaths'], 'A missing parent volume is not proof of native recycle success')
            self.assertTrue(unknown['warnings'])
            self.assertNotIn('activeTrash', worker.request('workspace.getSnapshot')['result'])

    def test_33_trash_rejects_arbitrary_paths_symlink_and_stale_revision(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('guard-trash/DEMO-001.mp4', 'DEMO-001', artwork=True)
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            current = worker.edit(job, {'title': 'Changed review'})
            params = {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'artworkId': 'local-poster'}
            self.reason(worker.request('artwork.prepareTrash', params), 'REVISION_CONFLICT')
            params.update(expectedReviewRevision=current['reviewRevision'], artworkId=str(video))
            self.reason(worker.request('artwork.prepareTrash', params), 'ARTWORK_NOT_LOCAL')
            poster = video.with_name(video.stem + '-poster.png'); original = poster.read_bytes()
            outside = Path(self.temp.name) / 'unrelated-image.png'; outside.write_bytes(original)
            poster.unlink(); poster.symlink_to(outside)
            params['artworkId'] = 'local-poster'
            self.assertIn('error', worker.request('artwork.prepareTrash', params))
            self.assertTrue(outside.is_file()); self.assertEqual(original, outside.read_bytes())
            self.assertNotIn('activeTrash', worker.request('workspace.getSnapshot')['result'])
            self.assertFalse(worker.get_job(job['jobId'])['busy'])

    def test_34_trash_refresh_preserves_online_cover_and_manual_fields(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('online-trash/DEMO-001.mp4', 'DEMO-001', artwork=True)
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            done = self.browser_import(worker, job)
            job = worker.edit(done['result']['job'], {'plot': 'Unsaved plot after explicit browser import'})
            selected_before = next(a for a in job['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'fanart')
            self.assertTrue(selected_before['selected'])
            fanart = next(a for a in job['artwork'] if a['artworkId'] == 'local-fanart')
            preview = worker.request('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': fanart['artworkId']})['result']
            self.assertTrue(preview['isLowResolution'])
            poster_preview = worker.request('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': 'local-poster'})['result']
            self.assertFalse(poster_preview['isLowResolution'])
            lease = self.prepare_trash(worker, job, 'local-poster'); token = {'trashToken': lease['trashToken']}
            worker.request('artwork.validateTrash', token)
            Path(lease['paths'][0]).rename(Path(self.temp.name) / 'simulated-local-trash.png')
            worker.request('artwork.completeTrash', token)
            current = worker.get_job(job['jobId'])
            self.assertEqual(job['metadata']['plot'], current['metadata']['plot'])
            self.assertEqual(job['metadata']['title'], current['metadata']['title'])
            self.assertEqual(job['metadata']['coverUrl'], current['metadata']['coverUrl'])
            self.assertEqual(job['candidates'], current['candidates'])
            self.assertTrue(next(a for a in current['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'fanart')['selected'])

    def test_35_async_artwork_stalled_http_cancels_and_bounds_concurrency(self):
        image = (ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes()
        release = threading.Event()
        state = {'requests': 0, 'mode': 'stall'}
        state_lock = threading.Lock()

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                with state_lock:
                    state['requests'] += 1
                    mode = state['mode']
                self.send_response(200)
                self.send_header('Content-Type', 'image/png')
                self.send_header('Content-Length', str(21 * 1024 * 1024 if mode == 'oversize' else len(image) if mode == 'image' else 4096))
                self.end_headers()
                if mode == 'stall':
                    self.wfile.write(b'x'); self.wfile.flush()
                    release.wait(10)
                elif mode == 'image':
                    self.wfile.write(image)

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        server.daemon_threads = True
        server_thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True)
        server_thread.start()
        try:
            with Worker(self.temp.name) as worker:
                _, job = worker.import_video()
                html = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace('https://example.invalid/syntheticpl.jpg', f'http://127.0.0.1:{server.server_port}/syntheticpl.jpg')
                done = self.browser_import(worker, job, html=html)
                self.assertEqual('completed', done['status'], done)
                job = done['result']['job']
                artwork = next(a for a in job['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'fanart')
                params = {'jobId': job['jobId'], 'artworkId': artwork['artworkId'], 'async': True}
                # Task.Run scheduling is not FIFO: occupy both slots before submitting
                # the known waiter, so reverse cancellation cannot free a slot first.
                operations = [worker.request('artwork.getPreview', params)['result']['operationId'] for _ in range(2)]
                deadline = time.monotonic() + 3
                while state['requests'] < 2 and time.monotonic() < deadline:
                    time.sleep(.01)
                self.assertEqual(2, state['requests'], 'Only two preview downloads may run concurrently')
                operations.append(worker.request('artwork.getPreview', params)['result']['operationId'])
                started = time.monotonic()
                snapshot = worker.request('workspace.getSnapshot')['result']
                self.assertLess(time.monotonic() - started, 1, 'Stalled image body must not block the RPC dispatcher')
                previews = [o for o in snapshot['activeOperations'] if o['operationId'] in operations]
                self.assertEqual(3, len(previews))
                self.assertTrue(all(o['readOnly'] for o in previews))
                self.assertFalse(worker.get_job(job['jobId'])['busy'])
                # Cancel the queued operation first, then both stalled network bodies.
                started = time.monotonic()
                for operation_id in reversed(operations):
                    worker.request('operations.cancel', {'operationId': operation_id})
                for operation_id in operations:
                    finished = worker.operation(operation_id, timeout=3)
                    self.assertEqual('canceled', finished['status'], finished)
                self.assertLess(time.monotonic() - started, 3, 'Cancellation must not wait for the twenty-second deadline')
                self.assertEqual(2, state['requests'], 'Canceled slot waiter must not start a request')
                self.assertEqual(job['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
                self.assertEqual([], list(Path(worker.initialized['cacheRoot']).glob('*.tmp')))
                with state_lock:
                    state['mode'] = 'image'
                release.set()
                success_id = worker.request('artwork.getPreview', params)['result']['operationId']
                success = worker.operation(success_id)
                self.assertEqual('completed', success['status'], success)
                self.assertTrue(success['readOnly'])
                self.assertEqual((5, 2), (success['result']['width'], success['result']['height']))
                self.assertEqual(image, Path(success['result']['path']).read_bytes())
                with state_lock:
                    state['mode'] = 'oversize'
                oversized_id = worker.request('artwork.getPreview', params)['result']['operationId']
                oversized = worker.operation(oversized_id)
                self.assertEqual('failed', oversized['status'], oversized)
                self.reason({'error': oversized['error']}, 'ARTWORK_INVALID')
                self.assertEqual(job['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
        finally:
            release.set(); server.shutdown(); server.server_close(); server_thread.join(timeout=2)

    def test_36_native_default_empty_custom_root_before_and_after_trash(self):
        with Worker(self.temp.name) as worker:
            video = self.make_media('native-default/DEMO-001.mp4', 'DEMO-001', artwork=True)
            job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            options = {'writeNfo': True, 'downloadPoster': False, 'downloadFanart': False, 'downloadExtrafanart': False,
                       'replaceLocalExtrafanart': False, 'includeIdInTitle': True, 'overwriteExisting': False,
                       'targetMode': 'VideoDirectory', 'renameVideo': False, 'customRootDirectory': ''}

            def native_preview(current):
                start = worker.request('save.preview', {'jobId': current['jobId'], 'expectedReviewRevision': current['reviewRevision'], 'options': options, 'async': True})
                self.assertIn('result', start, start)
                done = worker.operation(start['result']['operationId'])
                self.assertEqual('completed', done['status'], done)
                return done['result']

            first = native_preview(job)
            self.assertFalse(first['canExecute'], 'Existing NFO still requires overwrite consent')
            job = worker.get_job(job['jobId'])
            lease = self.prepare_trash(worker, job, 'local-poster'); token = {'trashToken': lease['trashToken']}
            worker.request('artwork.validateTrash', token)
            Path(lease['paths'][0]).rename(Path(self.temp.name) / 'simulated-trash-default.png')
            worker.request('artwork.completeTrash', token)
            second = native_preview(worker.get_job(job['jobId']))
            self.assertFalse(second['canExecute'])
            options['overwriteExisting'] = True
            ready = native_preview(worker.get_job(job['jobId']))
            self.assertTrue(ready['canExecute'], ready)
            saved = self.execute(worker, ready)
            self.assertEqual('completed', saved['result']['outcome'], saved)
            self.assertTrue(video.is_file())
            self.assertEqual('KEEP UNKNOWN XML', ET.parse(video.with_suffix('.nfo')).getroot().find('customNode/nested').text)
            options['targetMode'] = 'SourceNumberFolder'
            source_folder = native_preview(worker.get_job(job['jobId']))
            self.assertTrue(source_folder['canExecute'], source_folder)
            options['targetMode'] = 'CustomRootNumberFolder'
            response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': worker.get_job(job['jobId'])['reviewRevision'], 'options': options})
            self.reason(response, 'VOLUME_UNAVAILABLE')

    def test_37_late_source_nfo_requires_reimport_and_preserves_unknown_xml(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video(nfo=None)
            original_video = video.read_bytes()
            job = worker.edit(job, {'title': 'Preserve this unsaved review'})
            nfo = video.with_suffix('.nfo')
            external_xml = '<movie external="retain"><id>DEMO-001</id><title>External title</title><unknown>KEEP EXTERNAL XML</unknown></movie>'
            nfo.write_text(external_xml)
            response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'options': {'overwriteExisting': True, 'writeNfo': True}})
            self.reason(response, 'SOURCE_CHANGED')
            current = worker.get_job(job['jobId'])
            self.assertEqual(job['reviewRevision'], current['reviewRevision'])
            self.assertEqual(job['metadata'], current['metadata'])
            self.assertEqual(external_xml, nfo.read_text())
            self.assertEqual(original_video, video.read_bytes())
            worker.request('jobs.remove', {'jobId': job['jobId']})
            reloaded = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
            reloaded = worker.edit(reloaded, {'title': 'Reviewed after explicit reimport'})
            done = self.execute(worker, worker.preview(reloaded))
            self.assertEqual('completed', done['result']['outcome'], done)
            xml = ET.parse(nfo).getroot()
            self.assertEqual('KEEP EXTERNAL XML', xml.findtext('unknown'))
            self.assertEqual('retain', xml.attrib['external'])
            self.assertEqual(original_video, video.read_bytes())

    def test_38_late_nfo_before_execute_and_unread_destination_are_blocked(self):
        with Worker(self.temp.name) as worker:
            video, job = worker.import_video(nfo=None)
            original_video = video.read_bytes()
            plan = worker.preview(job)
            folder_nfo = video.parent / 'movie.nfo'
            external_xml = '<movie><unknown>ARRIVED AFTER PREVIEW</unknown></movie>'
            folder_nfo.write_text(external_xml)
            self.save_failure(worker, plan['planId'], 'SOURCE_CHANGED')
            self.assertEqual(external_xml, folder_nfo.read_text())
            self.assertFalse(video.with_suffix('.nfo').exists())
            self.assertEqual(original_video, video.read_bytes())
            folder_nfo.unlink()
            target = video.parent / 'DEMO-001' / 'DEMO-001.nfo'
            target.parent.mkdir(); target.write_text(external_xml)
            current = worker.get_job(job['jobId'])
            response = worker.request('save.preview', {'jobId': current['jobId'], 'expectedReviewRevision': current['reviewRevision'],
                'options': {'overwriteExisting': True, 'targetMode': 'SourceNumberFolder', 'renameVideo': True}})
            self.reason(response, 'SOURCE_CHANGED')
            self.assertEqual(external_xml, target.read_text())
            self.assertEqual(original_video, video.read_bytes())
            self.assertFalse(target.with_suffix('.mp4').exists())

    def test_39_changed_source_content_and_unselected_link_alias_are_rechecked(self):
        with Worker(self.temp.name) as worker:
            first = self.make_media('fresh-scan-a/DEMO-001.mp4', 'DEMO-001')
            second = self.make_media('fresh-scan-b/DEMO-002.mp4', 'DEMO-002')
            ids = worker.import_paths([first, second])['importedJobIds']
            jobs = {worker.get_job(i)['metadata']['id']: worker.get_job(i) for i in ids}
            nfo = first.with_suffix('.nfo')
            external = nfo.read_text().replace('</movie>', '<lateUnknown>KEEP LATE XML</lateUnknown></movie>')
            nfo.write_text(external)
            response = worker.request('save.preview', {'jobId': jobs['DEMO-001']['jobId'], 'expectedReviewRevision': jobs['DEMO-001']['reviewRevision'], 'options': {'overwriteExisting': True}})
            self.reason(response, 'SOURCE_CHANGED')
            self.assertEqual(external, nfo.read_text())
            # Fresh whole-queue protection must see an alias added in an unselected job's folder.
            alias = first.parent / 'alias.nfo'; alias.symlink_to(nfo.resolve())
            other = jobs['DEMO-002']
            response = worker.request('save.preview', {'jobId': other['jobId'], 'expectedReviewRevision': other['reviewRevision'], 'options': {'overwriteExisting': True}})
            self.assertIn('error', response, response)
            self.assertEqual(external, nfo.read_text())
            alias.unlink()
            outside = Path(self.temp.name) / 'outside.nfo'; outside.write_text('<movie><unknown>KEEP OUTSIDE</unknown></movie>')
            nfo.unlink(); nfo.symlink_to(outside)
            current = worker.get_job(jobs['DEMO-001']['jobId'])
            response = worker.request('save.preview', {'jobId': current['jobId'], 'expectedReviewRevision': current['reviewRevision'], 'options': {'overwriteExisting': True}})
            self.reason(response, 'SOURCE_CHANGED')
            self.assertEqual('<movie><unknown>KEEP OUTSIDE</unknown></movie>', outside.read_text())
            self.assertTrue(first.is_file() and second.is_file())

    def test_40_source_ancestor_redirect_before_preview_never_adopts_other_movie(self):
        for loaded_nfo in (False, True):
            with self.subTest(loaded_nfo=loaded_nfo):
                directory = Path(self.temp.name) / str(loaded_nfo)
                with Worker(directory) as worker:
                    video, job = worker.import_video(nfo=NFO if loaded_nfo else None)
                    original_bytes = video.read_bytes()
                    second_video = directory / 'other-job' / 'DEMO-002.mp4'
                    second_video.parent.mkdir(); second_video.write_bytes(b'SECOND UNCHANGED VIDEO')
                    second_job = worker.get_job(worker.import_paths([second_video])['importedJobIds'][0])
                    unrelated = directory / 'unrelated'
                    unrelated.mkdir()
                    (unrelated / video.name).write_bytes(b'UNRELATED SYNTHETIC VIDEO')
                    if loaded_nfo:
                        (unrelated / video.with_suffix('.nfo').name).write_text(NFO)
                    unrelated_before = {path.name: path.read_bytes() for path in unrelated.iterdir()}
                    preserved = video.parent.with_name('preserved-original')
                    video.parent.rename(preserved)
                    video.parent.symlink_to(unrelated.resolve(), target_is_directory=True)
                    response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'options': {'overwriteExisting': True}})
                    self.reason(response, 'SOURCE_CHANGED')
                    self.assertEqual(job['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
                    self.assertEqual(job['metadata'], worker.get_job(job['jobId'])['metadata'])
                    self.assertEqual(original_bytes, (preserved / video.name).read_bytes())
                    self.assertEqual(unrelated_before, {path.name: path.read_bytes() for path in unrelated.iterdir()})
                    other_response = worker.request('save.preview', {'jobId': second_job['jobId'], 'expectedReviewRevision': second_job['reviewRevision'], 'options': {'overwriteExisting': True}})
                    self.reason(other_response, 'SOURCE_CHANGED')
                    self.assertEqual(second_job['reviewRevision'], worker.get_job(second_job['jobId'])['reviewRevision'])
                    # Restoring the parent and redirecting only the registered video is
                    # equally unsafe, even when the replacement is a canonical target.
                    video.parent.unlink()
                    preserved.rename(video.parent)
                    original_video = video.with_suffix('.original')
                    video.rename(original_video)
                    video.symlink_to((unrelated / video.name).resolve())
                    response = worker.request('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'options': {'overwriteExisting': True}})
                    self.reason(response, 'SOURCE_CHANGED')
                    self.assertEqual(job['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
                    self.assertEqual(job['metadata'], worker.get_job(job['jobId'])['metadata'])
                    self.assertEqual(original_bytes, original_video.read_bytes())
                    self.assertEqual(unrelated_before, {path.name: path.read_bytes() for path in unrelated.iterdir()})
                    self.assertEqual(b'SECOND UNCHANGED VIDEO', second_video.read_bytes())


    def test_41_registered_cover_fallback_preserves_bounds_revision_and_source_identity(self):
        image = (ROOT / 'fixtures/macos/synthetic-cover-5x2.png').read_bytes()
        oversized_pixels = bytearray(image)
        oversized_pixels[16:20] = struct.pack('>I', 40_000_001)
        oversized_pixels[29:33] = struct.pack('>I', zlib.crc32(oversized_pixels[12:29]) & 0xffffffff)
        state = {'mode': 'fallback', 'requests': []}
        state_lock = threading.Lock()

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass

            def do_GET(self):
                with state_lock:
                    mode = state['mode']; state['requests'].append(self.path)
                missing = mode == 'missing' or mode == 'fallback' and self.path.endswith('pl.jpg') or mode == 'poster-fallback' and self.path.endswith('ps.jpg')
                body = b'not an image' if mode == 'invalid' else bytes(oversized_pixels) if mode == 'pixels' else image
                self.send_response(404 if missing else 200)
                self.send_header('Content-Type', 'image/png')
                self.send_header('Content-Length', str(21 * 1024 * 1024 if mode == 'oversize' else 0 if missing else len(body)))
                self.end_headers()
                if not missing and mode != 'oversize': self.wfile.write(body)

        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        server.daemon_threads = True
        thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True)
        thread.start()
        try:
            with Worker(self.temp.name) as worker:
                video, job = worker.import_video()
                untouched, other = worker.import_video(name='other/DEMO-002.mp4', nfo=NFO.replace('DEMO-001', 'DEMO-002'))
                base = f'http://127.0.0.1:{server.server_port}'
                template = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text()
                html = template.replace('https://example.invalid/syntheticpl.jpg', base + '/syntheticpl.jpg')
                imported = self.browser_import(worker, job, html=html)
                self.assertEqual('completed', imported['status'], imported)
                job = imported['result']['job']
                other_html = template.replace('DEMO-001', 'DEMO-002').replace('https://example.invalid/syntheticpl.jpg', base + '/otherpl.jpg')
                other_import = self.browser_import(worker, other, html=other_html)
                self.assertEqual('completed', other_import['status'], other_import)
                other = other_import['result']['job']
                files = {path: path.read_bytes() for path in (video, video.with_suffix('.nfo'), untouched, untouched.with_suffix('.nfo'))}
                cover = next(a for a in job['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'fanart')
                poster = next(a for a in job['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'poster')
                params = {'jobId': job['jobId'], 'artworkId': cover['artworkId'], 'async': True}

                def preview(arguments):
                    response = worker.request('artwork.getPreview', arguments)
                    self.assertIn('result', response, response)
                    return worker.operation(response['result']['operationId'])

                success = preview(params)
                self.assertEqual('completed', success['status'], success)
                actual = success['result']
                self.assertTrue(actual['usedFallback'])
                self.assertEqual(base + '/syntheticps.jpg', actual['resolvedLocation'])
                self.assertEqual(['httpError', 'succeeded'], [a['status'] for a in actual['attempts']])
                self.assertEqual([404, 200], [a['httpStatus'] for a in actual['attempts']])
                self.assertEqual([base + '/syntheticpl.jpg', base + '/syntheticps.jpg'], [a['location'] for a in actual['attempts']])
                self.assertEqual(image, Path(actual['path']).read_bytes())
                self.assertEqual(hashlib.sha256(image).hexdigest(), actual['cacheId'])
                self.assertEqual((5, 2), (actual['width'], actual['height']))
                self.assertEqual(['/syntheticpl.jpg', '/syntheticps.jpg'], state['requests'])
                request_count = len(state['requests'])
                self.reason(worker.request('artwork.getPreview', {**params, 'url': base + '/unregistered'}), 'INVALID_PARAMS')
                self.reason(worker.request('artwork.getPreview', {**params, 'artworkId': base + '/unregistered'}), 'ARTWORK_NOT_FOUND')
                self.assertEqual(request_count, len(state['requests']), 'Request-supplied URLs must never be fetched')

                # Poster prioritizes its registered poster URL; then only this source's cover.
                with state_lock: state.update(mode='poster-fallback', requests=[])
                poster_result = preview({**params, 'artworkId': poster['artworkId']})
                self.assertEqual('completed', poster_result['status'], poster_result)
                self.assertTrue(poster_result['result']['usedFallback'])
                self.assertEqual(base + '/syntheticpl.jpg', poster_result['result']['resolvedLocation'])
                self.assertEqual(['/syntheticps.jpg', '/syntheticpl.jpg'], state['requests'])

                # A rejected size or decoder limit is terminal, even with a usable fallback.
                for mode in ('oversize', 'invalid', 'pixels'):
                    with self.subTest(mode=mode):
                        with state_lock: state.update(mode=mode, requests=[])
                        rejected = preview(params)
                        self.assertEqual('failed', rejected['status'], rejected)
                        self.reason({'error': rejected['error']}, 'ARTWORK_INVALID')
                        self.assertEqual(['/syntheticpl.jpg'], state['requests'], 'Invalid/oversized content must not trigger fallback')
                with state_lock: state.update(mode='missing', requests=[])
                failed = preview(params)
                self.assertEqual('failed', failed['status'], failed)
                self.reason({'error': failed['error']}, 'OPERATION_FAILED')
                self.assertEqual(2, len(failed['error']['data']['detail']['attempts']))
                self.assertEqual(['/syntheticpl.jpg', '/syntheticps.jpg'], state['requests'])
                with state_lock: state.update(mode='valid', requests=[])
                direct = preview(params)
                self.assertEqual('completed', direct['status'], direct)
                self.assertFalse(direct['result']['usedFallback'])
                self.assertEqual(base + '/syntheticpl.jpg', direct['result']['resolvedLocation'])
                self.assertEqual(['/syntheticpl.jpg'], state['requests'])
                for original in (job, other):
                    current = worker.get_job(original['jobId'])
                    self.assertEqual(original['reviewRevision'], current['reviewRevision'])
                    self.assertEqual(original['metadata'], current['metadata'])
                self.assertEqual(files, {path: path.read_bytes() for path in files})
                self.assertEqual([], list(Path(worker.initialized['cacheRoot']).glob('*.tmp')))
        finally:
            server.shutdown(); server.server_close(); thread.join(timeout=2)


    def test_47_T06_R09_unopened_local_cover_quality_is_advisory_and_not_poster_derived(self):
        with Worker(self.temp.name) as worker:
            for movie, dimensions, expected in [('FNS-121', (147, 200), 'lowResolution'), ('TEST-901', (257, 126), 'lowResolution'),
                                                  ('TEST-902', (200, 200), 'lowResolution'), ('TEST-903', (800, 535), 'ok'),
                                                  ('TEST-904', None, 'unknown'), ('TEST-905', None, 'unknown')]:
                with self.subTest(movie=movie):
                    video = Path(self.temp.name) / movie / f'{movie}.mp4'
                    video.parent.mkdir(); video.write_bytes(b'SYNTHETIC VIDEO')
                    (video.parent / 'poster.png').write_bytes(synthetic_png(1200, 1800))
                    if movie != 'TEST-905':
                        (video.parent / 'fanart.png').write_bytes(synthetic_png(*dimensions) if dimensions else b'invalid image')
                    if movie == 'FNS-121':
                        (video.parent / 'extrafanart').mkdir()
                        (video.parent / 'extrafanart' / 'local.png').write_bytes(synthetic_png(16, 12))
                    job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
                    self.assertEqual(expected, job['coverQuality']['state'])
                    if dimensions: self.assertEqual(dimensions, (job['coverQuality']['width'], job['coverQuality']['height']))
                    else: self.assertNotIn('width', job['coverQuality'])
                    # No preview was requested. Advisory quality never blocks ordinary NFO output.
                    revision = job['reviewRevision']
                    self.assertEqual(revision, worker.get_job(job['jobId'])['reviewRevision'])
                    plan = worker.preview(job)
                    self.assertTrue(plan['canExecute'], plan)
                    self.assertEqual(plan['reviewRevision'], worker.get_job(job['jobId'])['reviewRevision'])
                    before = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in video.parent.iterdir() if path.is_file()}
                    saved = worker.operation(worker.request('save.execute', {'planId': plan['planId']})['result']['operationId'])
                    self.assertEqual('completed', saved['result']['outcome'], saved)
                    current = worker.get_job(job['jobId'])
                    self.assertEqual('Completed', current['saveState'])
                    self.assertEqual(expected, current['coverQuality']['state'], 'NFO-only save must retain freshly measured local fanart quality')
                    if dimensions:
                        self.assertEqual(dimensions, (current['coverQuality']['width'], current['coverQuality']['height']))
                        selected_local = next(a for a in current['artwork'] if a['kind'] == 'fanart' and a['source'] == 'local' and a['selected'])
                        self.assertEqual(selected_local['candidateSource'], current['coverQuality']['source'])
                    else: self.assertNotIn('width', current['coverQuality'])
                    saved_revision = current['reviewRevision']
                    self.assertGreaterEqual(saved_revision, plan['reviewRevision'])
                    self.assertEqual(current, worker.get_job(job['jobId']), 'Reading quality must not mutate the review revision')
                    for name, digest in before.items():
                        self.assertEqual(digest, hashlib.sha256((video.parent / name).read_bytes()).hexdigest())
            self.assertEqual({'.owner.json', '.owner.lock'}, {p.name for p in Path(worker.initialized['cacheRoot']).iterdir()})
            self.assertEqual(0, worker.request('workspace.getSnapshot')['result']['artworkCache']['bytes'])
            # Retaining a reviewed remote source while organizing without NFO output
            # must not attach the freshly reloaded local fanart's dimensions to it.
            fns = next(j for j in worker.request('workspace.getSnapshot')['result']['jobs'] if j['metadata']['id'] == 'FNS-121')
            html = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace('DEMO-001', 'FNS-121')
            remote = self.browser_import(worker, fns, html=html)['result']['job']
            self.assertEqual('unknown', remote['coverQuality']['state'])
            sample = next(a for a in remote['artwork'] if a['kind'] == 'sample' and a['source'] == 'local')
            remote = worker.request('artwork.select', {'jobId': remote['jobId'], 'expectedReviewRevision': remote['reviewRevision'], 'artworkId': sample['artworkId'], 'selected': True})['result']
            plan = worker.preview(remote, writeNfo=False, downloadExtrafanart=True, targetMode='SourceNumberFolder', renameVideo=True)
            self.assertTrue(plan['canExecute'], plan)
            saved = self.execute(worker, plan)
            self.assertEqual('completed', saved['result']['outcome'], saved)
            retained = worker.get_job(fns['jobId'])
            self.assertEqual('javlibrary', retained['coverQuality']['source'])
            self.assertEqual('unknown', retained['coverQuality']['state'])
            self.assertNotIn('width', retained['coverQuality'])
            self.assertTrue(retained['hasUnsavedChanges'])
            self.assertEqual(retained, worker.get_job(fns['jobId']))

    def test_49_X01_X02_scan_diagnostics_keep_readable_movies_and_full_path(self):
        with Worker(self.temp.name) as worker:
            root = Path(self.temp.name) / 'scan'
            readable = root / 'readable' / 'TEST-910.mp4'
            readable.parent.mkdir(parents=True); readable.write_bytes(b'SYNTHETIC VIDEO')
            denied = root / ('permission-' + 'x' * 80)
            denied.mkdir(); (denied / 'TEST-911.mp4').write_bytes(b'UNREADABLE SYNTHETIC VIDEO')
            missing = root / ('missing-' + 'y' * 160)
            denied.chmod(0)
            try:
                result = worker.import_paths([root, missing])
                self.assertEqual(1, len(result['importedJobIds']), result)
                diagnostics = result['diagnostics']
                blocked = next(d for d in diagnostics if Path(d['path']).name == denied.name)
                absent = next(d for d in diagnostics if Path(d['path']).name == missing.name)
                self.assertEqual('AccessDenied', blocked['kind']); self.assertTrue(blocked['message'])
                self.assertEqual('PathUnavailable', absent['kind'])
                self.assertEqual(str(denied), blocked['path']); self.assertEqual(str(missing), absent['path'])
                self.assertIn(str(denied), '\n'.join(result['warnings']))
            finally: denied.chmod(0o700)

    def test_52_R06_R09_stale_or_canceled_remote_dimensions_cannot_replace_current_job_quality(self):
        entered = threading.Event(); release = threading.Event(); image = synthetic_png(800, 535)
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                self.send_response(200); self.send_header('Content-Length', str(len(image))); self.end_headers()
                entered.set(); release.wait(5)
                try: self.wfile.write(image)
                except (BrokenPipeError, ConnectionResetError): pass
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler); server.daemon_threads = True
        thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True); thread.start()
        try:
            with Worker(self.temp.name) as worker:
                video = Path(self.temp.name) / 'local' / 'DEMO-001.mp4'; video.parent.mkdir(); video.write_bytes(b'SYNTHETIC VIDEO')
                (video.parent / 'fanart.png').write_bytes(synthetic_png(147, 200))
                job = worker.get_job(worker.import_paths([video])['importedJobIds'][0])
                _, other = worker.import_video('other/DEMO-002.mp4', nfo=None)
                html = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace('https://example.invalid/syntheticpl.jpg', f'http://127.0.0.1:{server.server_port}/syntheticpl.jpg')
                job = self.browser_import(worker, job, html=html)['result']['job']
                remote = next(a for a in job['artwork'] if a['source'] == 'javlibrary' and a['kind'] == 'fanart')
                params = {'jobId': job['jobId'], 'artworkId': remote['artworkId'], 'async': True}
                operation = worker.request('artwork.getPreview', params)['result']['operationId']; self.assertTrue(entered.wait(3))
                local = worker.request('artwork.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': 'local'})['result']
                local_id = next(a['artworkId'] for a in local['artwork'] if a['kind'] == 'fanart' and a['source'] == 'local')
                worker.request('artwork.getPreview', {'jobId': local['jobId'], 'artworkId': local_id})
                expected = worker.get_job(job['jobId'])
                self.assertEqual('lowResolution', expected['coverQuality']['state'])
                release.set(); self.assertEqual('completed', worker.operation(operation)['status'])
                self.assertEqual(expected, worker.get_job(job['jobId']), 'Late old-source dimensions must not replace current quality or revision')
                self.assertEqual(other, worker.get_job(other['jobId']), 'A preview cannot mutate a different job')
                remote_job = worker.request('artwork.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': expected['reviewRevision'], 'source': 'javlibrary'})['result']
                self.assertEqual('unknown', remote_job['coverQuality']['state'])
                entered.clear(); release.clear()
                operation = worker.request('artwork.getPreview', params)['result']['operationId']; self.assertTrue(entered.wait(3))
                worker.request('operations.cancel', {'operationId': operation})
                self.assertEqual('canceled', worker.operation(operation, timeout=3)['status'])
                release.set()
                self.assertEqual(remote_job, worker.get_job(job['jobId']))
                self.assertEqual([], list(Path(worker.initialized['cacheRoot']).glob('*.tmp')))
        finally:
            release.set(); server.shutdown(); server.server_close(); thread.join(timeout=2)


    def test_54_T01_T02_P04_discovery_is_read_only_and_imports_only_checked_items(self):
        incoming = Path(self.temp.name) / 'ABF-391 parent'; incoming.mkdir()
        names = {'489155.com@ABF-391-U.mp4': 'ABF-391', 'MIDE-720 合成 OL 标题.mp4': 'MIDE-720',
                 '无番号合成资料.mp4': '', 'MFYD-190-U.mp4': 'MFYD-190', 'MFYD-190-UC.mp4': 'MFYD-190'}
        for name in names: (incoming / name).write_bytes(b'KNOWN SYNTHETIC INPUT')
        # Invalid NFO must not be parsed in the discovery phase, and an unselected
        # recognized item must not be loaded merely because its directory was scanned.
        local_nfo = incoming / 'MIDE-720 合成 OL 标题.nfo'; local_nfo.write_bytes(b'INVALID XML ONLY')
        originals = {p: p.read_bytes() for p in incoming.iterdir()}
        with Worker(self.temp.name) as worker:
            existing_path, existing = worker.import_video('existing/TEST-501.mp4', nfo=None)
            existing = worker.edit(existing, {'title': 'Unsaved review retained'})
            before = worker.request('workspace.getSnapshot')['result']['jobs']
            self.assertTrue(worker.initialized['capabilities']['discoverInputs'])
            discovery = worker.discover_paths([incoming, incoming / next(iter(names)), existing_path])
            self.assertEqual(before, worker.request('workspace.getSnapshot')['result']['jobs'])
            self.assertEqual([], discovery['diagnostics'])
            items = {Path(i['primaryPath']).name: i for i in discovery['items']}
            for name, expected in names.items():
                self.assertEqual(expected, items[name]['movieId']); self.assertTrue(items[name]['canImport'])
            self.assertTrue(items[existing_path.name]['alreadyImported']); self.assertFalse(items[existing_path.name]['canImport'])
            selected = [items['489155.com@ABF-391-U.mp4']['itemId'], items['无番号合成资料.mp4']['itemId']]
            done = worker.confirm_discovery(discovery, selected)
            self.assertEqual('completed', done['status']); self.assertFalse(done['readOnly'])
            jobs = [worker.get_job(j) for j in done['result']['importedJobIds']]
            self.assertEqual(['ABF-391', ''], [j['metadata']['id'] for j in jobs])
            self.assertEqual(existing, worker.get_job(existing['jobId']))
            self.assertEqual(3, len(worker.request('workspace.getSnapshot')['result']['jobs']))
            self.assertEqual(originals, {p: p.read_bytes() for p in originals})

    def test_55_discovery_unknown_duplicate_empty_ids_discard_and_capacity_are_atomic(self):
        path = Path(self.temp.name) / 'TEST-510.mp4'; path.write_bytes(b'VIDEO')
        with Worker(self.temp.name) as worker:
            discoveries = [worker.discover_paths([path]) for _ in range(4)]
            self.reason(worker.request('workspace.discoverInputs', {'paths': [str(path)]}), 'DISCOVERY_CAPACITY')
            discovery = discoveries[0]; item_id = discovery['items'][0]['itemId']
            for ids in ([], ['unknown'], [item_id, item_id], [42]):
                self.reason(worker.request('workspace.importDiscovery', {'discoveryId': discovery['discoveryId'], 'selectedItemIds': ids}), 'INVALID_PARAMS')
            self.assertEqual([], worker.request('workspace.getSnapshot')['result']['jobs'])
            self.assertTrue(worker.request('workspace.discardDiscovery', {'discoveryId': discovery['discoveryId']})['result']['discarded'])
            self.reason(worker.request('workspace.importDiscovery', {'discoveryId': discovery['discoveryId'], 'selectedItemIds': [item_id]}), 'DISCOVERY_STALE')
            replacement = worker.discover_paths([path])
            done = worker.confirm_discovery(replacement, [replacement['items'][0]['itemId']])
            self.assertEqual('completed', done['status'])
            self.reason(worker.request('workspace.importDiscovery', {'discoveryId': replacement['discoveryId'], 'selectedItemIds': [replacement['items'][0]['itemId']]}), 'DISCOVERY_STALE')
            # Tokens may observe queue changes; confirmation rechecks duplicates without replacing review.
            current = worker.get_job(done['result']['importedJobIds'][0]); current = worker.edit(current, {'title': 'Keep current draft'})
            duplicate = worker.confirm_discovery(discoveries[1], [discoveries[1]['items'][0]['itemId']])
            self.assertEqual([], duplicate['result']['importedJobIds']); self.assertEqual([current['jobId']], duplicate['result']['duplicateJobIds'])
            self.assertEqual(current, worker.get_job(current['jobId']))

    def test_56_discovery_identity_replacement_or_link_redirect_rejects_entire_selection(self):
        for mutation in ('replace', 'symlink', 'ancestor', 'delete'):
            with self.subTest(mutation=mutation):
                root = Path(self.temp.name) / mutation; root.mkdir()
                folder = root / 'inputs'; folder.mkdir()
                first = folder / 'TEST-521.mp4'; second = folder / 'TEST-522.mp4'
                first.write_bytes(b'FIRST VIDEO'); second.write_bytes(b'SECOND VIDEO')
                with Worker(root / 'engine') as worker:
                    discovery = worker.discover_paths([folder]); originals = {p.name: p.read_bytes() for p in folder.iterdir()}
                    if mutation == 'replace':
                        st = second.stat(); replacement = folder / 'replacement'
                        replacement.write_bytes(second.read_bytes()); os.utime(replacement, ns=(st.st_atime_ns, st.st_mtime_ns)); replacement.replace(second)
                    elif mutation == 'symlink':
                        other = root / 'other.mp4'; other.write_bytes(second.read_bytes()); second.unlink(); second.symlink_to(other)
                    elif mutation == 'ancestor':
                        moved = root / 'moved'; folder.rename(moved); folder.symlink_to(moved, target_is_directory=True)
                    else: second.unlink()
                    done = worker.confirm_discovery(discovery, [i['itemId'] for i in discovery['items']])
                    self.assertEqual('failed', done['status']); self.assertEqual('SOURCE_CHANGED', done['error']['data']['reason'])
                    self.assertEqual([], worker.request('workspace.getSnapshot')['result']['jobs'])
                    self.assertEqual(originals[first.name], first.read_bytes())
                    self.assertEqual([], list(root.rglob('*.nfo')))

    def test_57_confirmed_discovery_never_rescans_new_movies_or_cd_parts_and_trash_blocks(self):
        folder = Path(self.temp.name) / 'parts'; folder.mkdir()
        first = folder / 'TEST-531-CD1.mp4'; second = folder / 'TEST-531-CD2.mp4'
        first.write_bytes(b'PART ONE'); second.write_bytes(b'PART TWO')
        with Worker(self.temp.name) as worker:
            discovery = worker.discover_paths([first])
            self.assertEqual(1, len(discovery['items'])); self.assertEqual(2, len(discovery['items'][0]['videoPaths']))
            late = folder / 'TEST-531-CD3.mp4'; late.write_bytes(b'UNREVIEWED THIRD')
            other = folder / 'TEST-532.mp4'; other.write_bytes(b'UNSELECTED OTHER')
            done = worker.confirm_discovery(discovery, [discovery['items'][0]['itemId']])
            job = worker.get_job(done['result']['importedJobIds'][0])
            self.assertEqual([str(first.resolve()), str(second.resolve())], job['videoPaths'])
            self.assertEqual(1, len(worker.request('workspace.getSnapshot')['result']['jobs']))
            # Pending discovery does not itself hold a filesystem lease; a later trash lease
            # blocks confirmation before any local metadata loading or queue mutation.
            art_video = self.make_media('trash/TEST-533.mp4', 'TEST-533', artwork=True)
            art_job = worker.get_job(worker.import_paths([art_video])['importedJobIds'][0])
            pending = worker.discover_paths([other]); lease = self.prepare_trash(worker, art_job, 'local-poster')
            self.reason(worker.request('workspace.discoverInputs', {'paths': [str(other)]}), 'TRASH_PENDING')
            self.reason(worker.request('workspace.importDiscovery', {'discoveryId': pending['discoveryId'], 'selectedItemIds': [pending['items'][0]['itemId']]}), 'TRASH_PENDING')
            worker.request('artwork.abortTrash', {'trashToken': lease['trashToken']})
            self.assertEqual('completed', worker.confirm_discovery(pending, [pending['items'][0]['itemId']])['status'])
            self.assertEqual(b'UNREVIEWED THIRD', late.read_bytes()); self.assertEqual(b'UNSELECTED OTHER', other.read_bytes())

    def test_58_discovery_private_filename_matrix_stays_outside_source_and_queue_until_confirmed(self):
        configured = os.environ.get('JAVMETALITE_PRIVATE_PARSING_CASES')
        cases = json.loads(Path(configured).read_text()) if configured else [
            {'caseId': 'public-A', 'name': 'mirror.example@IPZZ-950-UC.mp4', 'expectedId': 'IPZZ-950'},
            {'caseId': 'public-C', 'name': 'MIDE-720 Synthetic OL.mp4', 'expectedId': 'MIDE-720'},
            {'caseId': 'public-B', 'name': 'Synthetic advertisement.mp4', 'expectedId': None}]
        with Worker(self.temp.name) as worker:
            for index, case in enumerate(cases):
                folder = Path(self.temp.name) / f'case-{index}'; folder.mkdir()
                path = folder / case['name']; path.write_bytes(b'PRIVATE NAME / SYNTHETIC BYTES')
                discovery = worker.discover_paths([path])
                self.assertEqual(1, len(discovery['items']), case['caseId'])
                self.assertEqual(case['expectedId'] or '', discovery['items'][0]['movieId'], case['caseId'])
                self.assertTrue(discovery['items'][0]['canImport'], case['caseId'])
                self.assertEqual([], worker.request('workspace.getSnapshot')['result']['jobs'], case['caseId'])
                worker.request('workspace.discardDiscovery', {'discoveryId': discovery['discoveryId']})


@unittest.skipUnless(SEARCH_TEST_HOST, 'Fixed HTTP host absent; run test.sh for the complete provider gate')
class ProviderWorkerTests(unittest.TestCase):
    """Production Workspace + real provider parsers/scheduler, zero external HTTP."""
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='JavMetaLite fixed providers ')
        self.addCleanup(self.temp.cleanup)
        self.worker = Worker(self.temp.name, executable=SEARCH_TEST_HOST)
        self.addCleanup(self.worker.close)

    @staticmethod
    def libre_url(movie): return f'https://www.libredmm.com/movies/{movie}.json'

    @staticmethod
    def r18_url(content): return f'https://r18.dev/videos/vod/movies/detail/-/content_id={content}/json'

    @staticmethod
    def combined_url(content): return f'https://r18.dev/videos/vod/movies/detail/-/combined={content}/json'

    @staticmethod
    def compact_url(movie): return f'https://r18.dev/videos/vod/movies/detail/-/dvd_id={movie.replace("-", "").lower()}/json'

    @staticmethod
    def libre(movie, title='Synthetic Libre title', **extra):
        return {'normalized_id': movie, 'title': title, 'subtitle': movie.replace('-', '').lower(), **extra}

    @staticmethod
    def r18(movie, content, title='Synthetic R18 title', **extra):
        return {'dvd_id': movie, 'content_id': content, 'title': title, **extra}

    def configure(self, routes=None, worker=None, **options):
        self.assertIn('result', (worker or self.worker).request('fixture.configure', {'routes': routes or [], **options}))

    @staticmethod
    def route(url, *responses): return {'url': url, 'responses': list(responses)}

    @staticmethod
    def ok(value): return {'status': 200, 'json': value}

    def requests(self, kind='http', worker=None):
        records = (worker or self.worker).request('fixture.requests')['result']['requests']
        return [record for record in records if record['kind'] == kind]

    def wait_requests(self, count=1, kind='http', worker=None):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            rows = self.requests(kind, worker=worker)
            if len(rows) >= count: return rows
            time.sleep(.005)
        self.fail(f'Missing {kind} fixture events: {self.requests(kind, worker=worker)}')

    def search(self, job, mode, retry=False):
        response = self.worker.request('jobs.retryFailedSources' if retry else 'jobs.search', {'jobId': job['jobId'], 'sourceMode': mode})
        self.assertIn('result', response, response)
        return self.worker.operation(response['result']['operationId'])

    def test_75_reused_preview_inventory_rejects_late_unselected_sidecar_without_publishing(self):
        media = [self.worker.import_video(f'inventory-preview/TEST-{801+i}.mp4', nfo=None) for i in range(2)]
        videos, jobs = [m[0] for m in media], [m[1] for m in media]
        originals = {p: p.read_bytes() for p in videos}
        selected = jobs[1]
        self.configure(holdSidecarInventory=True, sidecarInventoryPhase='save.preview', resetRequests=True)
        started = self.worker.request('save.preview', {'jobId': selected['jobId'], 'expectedReviewRevision': selected['reviewRevision'],
            'async': True, 'options': {'writeNfo': True}})['result']
        self.wait_requests(kind='sidecarInventoryCaptured')
        late = videos[0].with_suffix('.nfo'); late.write_text('<movie><id>TEST-801</id><title>Late unselected source</title></movie>')
        external = late.read_bytes()
        self.configure(holdSidecarInventory=False)
        done = self.worker.operation(started['operationId'])
        self.assertEqual('failed', done['status']); self.assertEqual('SOURCE_CHANGED', done['error']['data']['reason'])
        self.assertNotIn('result', done)
        self.assertEqual(selected, self.worker.get_job(selected['jobId']))
        self.assertEqual(external, late.read_bytes()); self.assertEqual(originals, {p: p.read_bytes() for p in videos})
        current = self.worker.get_job(selected['jobId'])
        fresh = self.worker.preview(current)
        self.assertTrue(fresh['canExecute'], 'A new operation must rescan rather than reuse the rejected inventory')
        self.assertFalse(videos[1].with_suffix('.nfo').exists())

    def test_76_reused_import_inventory_changes_admit_nothing_and_preserve_existing_review(self):
        _, existing = self.worker.import_video('existing/TEST-810.mp4', nfo=None)
        existing = self.worker.edit(existing, {'title': 'Preserve existing unsaved review'})
        folder = Path(self.temp.name) / 'inventory-import'; folder.mkdir()
        videos = [folder / f'TEST-{811+i}.mp4' for i in range(2)]
        for path in videos: path.write_bytes(b'SYNTHETIC INVENTORY INPUT')
        original_videos = {path:path.read_bytes() for path in videos}
        found = self.worker.discover_paths([folder])
        self.configure(holdSidecarInventory=True, sidecarInventoryPhase='importDiscovery', resetRequests=True)
        started = self.worker.request('workspace.importDiscovery', {'discoveryId': found['discoveryId'],
            'selectedItemIds': [item['itemId'] for item in found['items']]})['result']
        self.wait_requests(kind='sidecarInventoryCaptured')
        late = videos[0].with_suffix('.nfo'); late.write_text('<movie><id>TEST-811</id><title>Arrived after capture</title></movie>')
        external = late.read_bytes()
        self.configure(holdSidecarInventory=False)
        done = self.worker.operation(started['operationId'])
        self.assertEqual('failed', done['status']); self.assertEqual('SOURCE_CHANGED', done['error']['data']['reason'])
        self.assertEqual([existing], self.worker.request('workspace.getSnapshot')['result']['jobs'])
        self.assertEqual(external, late.read_bytes()); self.assertEqual(original_videos, {path:path.read_bytes() for path in videos})
        consumed = self.worker.request('workspace.importDiscovery', {'discoveryId': found['discoveryId'],
            'selectedItemIds': [item['itemId'] for item in found['items']]})
        self.assertEqual('DISCOVERY_STALE', consumed['error']['data']['reason'])
        fresh = self.worker.discover_paths([folder])
        admitted = self.worker.confirm_discovery(fresh, [item['itemId'] for item in fresh['items']])
        self.assertEqual('completed', admitted['status']); self.assertEqual(2, len(admitted['result']['importedJobIds']))
        loaded = [self.worker.get_job(job_id) for job_id in admitted['result']['importedJobIds']]
        self.assertTrue(any(job['metadata']['title'] == 'Arrived after capture' for job in loaded))
        self.assertEqual(existing, self.worker.get_job(existing['jobId']))

    def test_77_inventory_capture_cancellation_retains_review_and_never_admits_or_publishes(self):
        for phase in ('save.preview', 'importDiscovery'):
            with self.subTest(phase=phase):
                folder = Path(self.temp.name) / phase.replace('.', '-'); folder.mkdir()
                videos = [folder / f'TEST-{821+i}.mp4' for i in range(2)]
                for path in videos: path.write_bytes(b'UNCHANGED CANCELED INVENTORY')
                found = self.worker.discover_paths([folder])
                jobs = []
                if phase == 'save.preview':
                    admitted = self.worker.confirm_discovery(found, [item['itemId'] for item in found['items']])
                    jobs = [self.worker.get_job(job_id) for job_id in admitted['result']['importedJobIds']]
                self.configure(holdSidecarInventory=True, sidecarInventoryPhase=phase, resetRequests=True)
                if phase == 'save.preview':
                    params = {'jobId':jobs[0]['jobId'], 'expectedReviewRevision':jobs[0]['reviewRevision'], 'async':True, 'options':{'writeNfo':True}}
                else:
                    params = {'discoveryId':found['discoveryId'], 'selectedItemIds':[item['itemId'] for item in found['items']]}
                started = self.worker.request(phase if phase == 'save.preview' else 'workspace.importDiscovery', params)['result']
                self.wait_requests(kind='sidecarInventoryCaptured')
                self.worker.request('operations.cancel', started)
                done = self.worker.operation(started['operationId'], timeout=3)
                self.assertEqual('canceled',done['status']); self.assertNotIn('result',done)
                self.configure(holdSidecarInventory=False)
                self.assertEqual(jobs,self.worker.request('workspace.getSnapshot')['result']['jobs'])
                for path in videos: self.assertEqual(b'UNCHANGED CANCELED INVENTORY',path.read_bytes())
                self.assertEqual([],list(folder.glob('*.nfo')))
                if jobs: self.worker.request('jobs.remove', {'jobIds':[job['jobId'] for job in jobs]})

    def test_74_batch_query_cancel_retry_tail_then_revisioned_batch_save(self):
        media = [self.worker.import_video(f'batch/{i}/TEST-{851+i}.mp4', nfo=NFO.replace('DEMO-001', f'TEST-{851+i}')) for i in range(3)]
        videos, jobs = [m[0] for m in media], [m[1] for m in media]
        originals = {p: p.read_bytes() for video in videos for p in [video, video.with_suffix('.nfo')]}
        urls = [self.libre_url(f'TEST-{851+i}') for i in range(3)]
        self.configure([self.route(urls[0], self.ok(self.libre('TEST-851', title='Batch first queried'))),
                        self.route(urls[1], {'status': 200, 'waitForCancellation': True}),
                        self.route(urls[2], self.ok(self.libre('TEST-853', title='Batch third queried')))])
        started = self.worker.request('jobs.search', {'jobIds': [j['jobId'] for j in jobs], 'sourceMode': 'libredmm'})['result']['operationId']
        self.wait_requests(2)
        self.worker.request('operations.cancel', {'operationId': started})
        done = self.worker.operation(started)['result']
        self.assertEqual('canceled', done['outcome'])
        self.assertEqual(['Completed', 'Canceled', 'NotStarted'], [row['status'] for row in done['jobResults']])
        current = [self.worker.get_job(j['jobId']) for j in jobs]
        self.assertTrue(all(not j['busy'] for j in current))
        for i in (1, 2):
            self.assertEqual(jobs[i]['metadata'], current[i]['metadata']); self.assertEqual(jobs[i]['reviewRevision'], current[i]['reviewRevision'])
        self.assertEqual([urls[0], urls[1]], [r['url'] for r in self.requests()])
        self.configure([self.route(urls[i], self.ok(self.libre(f'TEST-{851+i}', title=f'Batch retry {i}'))) for i in (1, 2)])
        second = self.worker.request('jobs.search', {'jobIds': [current[i]['jobId'] for i in (1, 2)], 'sourceMode': 'libredmm'})['result']['operationId']
        retried = self.worker.operation(second)['result']
        self.assertEqual('completed', retried['outcome']); self.assertEqual(2, len(retried['jobResults']))
        self.assertEqual([urls[0], urls[1], urls[1], urls[2]], [r['url'] for r in self.requests()])
        self.assertEqual(originals, {p: p.read_bytes() for p in originals})
        current = [self.worker.get_job(j['jobId']) for j in jobs]
        def preview(overwrite):
            revisions = {j['jobId']: self.worker.get_job(j['jobId'])['reviewRevision'] for j in current}
            response = self.worker.request('save.preview', {'jobIds': [j['jobId'] for j in current], 'expectedReviewRevisions': revisions,
                'options': {'overwriteExisting': overwrite, 'includeIdInTitle': False}})
            self.assertIn('result', response, response); return response['result']
        self.assertFalse(preview(False)['canExecute']); self.assertEqual(originals, {p: p.read_bytes() for p in originals})
        plan = preview(True)
        last = self.worker.get_job(jobs[-1]['jobId']); self.worker.edit(last, {'rating': 'Explicit reviewed rating'})
        rejected = self.worker.request('save.execute', {'planId': plan['planId']})
        self.assertEqual('PLAN_STALE', rejected['error']['data']['reason']); self.assertEqual(originals, {p: p.read_bytes() for p in originals})
        plan = preview(True); saved = self.worker.operation(self.worker.request('save.execute', {'planId': plan['planId']})['result']['operationId'])
        self.assertEqual('completed', saved['result']['outcome']); self.assertEqual(3, len(saved['result']['savedJobIds']))
        for i, video in enumerate(videos):
            self.assertEqual(originals[video], video.read_bytes()); root = ET.parse(video.with_suffix('.nfo')).getroot()
            self.assertEqual('Batch first queried' if i == 0 else f'Batch retry {i}', root.findtext('title'))
            self.assertEqual('KEEP UNKNOWN XML', root.findtext('customNode/nested')); self.assertEqual('keep-me', root.attrib['customAttribute'])
        journals = [json.loads(p.read_text()) for p in (self.worker.data / 'recovery').glob('*.json')]
        self.assertEqual(3, len(journals)); self.assertTrue(all(j['status'] == 'completed' for j in journals))

    def test_72_id_change_drops_online_fields_artwork_and_candidates_before_new_query(self):
        video, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        original = video.read_bytes()
        self.configure([self.route(self.combined_url('ipx00081'), self.ok(self.r18('IPX-081', 'ipx00081',
            title='OLD MOVIE A', description='OLD PLOT A', jacket_full_url='https://example.invalid/movie-a.jpg',
            sample_images=[{'large': 'https://example.invalid/sample-a.jpg'}])))])
        self.search(job, 'r18dev'); job = self.worker.get_job(job['jobId'])
        self.assertIn('r18dev', job['reviewSources'])
        same = self.worker.edit(job, {'id': 'ipx-081'})
        self.assertIn('r18dev', same['reviewSources'], 'A spelling-only ID edit must retain the same movie cache')
        reviewed = self.worker.edit(same, {'maker': 'Explicit manual maker', 'plot': ''})
        old_plan = self.worker.preview(reviewed)
        reviewed = self.worker.get_job(job['jobId'])
        changed = self.worker.edit(reviewed, {'id': 'IPX-082'})
        self.assertGreater(changed['reviewRevision'], reviewed['reviewRevision'])
        self.assertEqual('IPX-082', changed['metadata']['id'])
        self.assertEqual('', changed['metadata']['title'])
        self.assertEqual('Explicit manual maker', changed['metadata']['maker'])
        self.assertEqual('', changed['metadata']['plot'])
        self.assertEqual('', changed['metadata']['contentId'])
        self.assertEqual('', changed['metadata']['sourceUrl'])
        self.assertNotIn('r18dev', changed['reviewSources'])
        self.assertFalse(any(c['source'] == 'r18dev' for c in changed['candidates']))
        self.assertFalse(any(a['source'] == 'r18dev' for a in changed['artwork']))
        targets = self.worker.request('browser.getTargets', {'jobId': changed['jobId']})['result']['targets']
        self.assertTrue(next(t for t in targets if t['source'] == 'r18dev')['requiresResolution'])
        self.assertFalse(any('ipx00081' in t['url'] for t in targets))
        self.assertEqual('PLAN_STALE', self.worker.request('save.execute', {'planId': old_plan['planId']})['error']['data']['reason'])
        rejected = self.worker.request('jobs.selectSource', {'jobId': changed['jobId'], 'expectedReviewRevision': changed['reviewRevision'], 'source': 'r18dev'})
        self.assertIn('error', rejected)
        self.assertEqual(changed['reviewRevision'], self.worker.get_job(job['jobId'])['reviewRevision'])
        self.configure([self.route(self.libre_url('IPX-082'), self.ok(self.libre('IPX-082', title='NEW MOVIE B')))])
        self.search(changed, 'libredmm'); current = self.worker.get_job(job['jobId'])
        self.assertEqual('NEW MOVIE B', current['metadata']['title'])
        self.assertEqual('', current['metadata']['plot'], 'Old online data must not be demoted into a manual fallback')
        self.assertEqual('Explicit manual maker', next(c['value'] for c in current['candidates'] if c['field'] == 'maker' and c['source'] == 'manual'))
        self.assertEqual(['libredmm'], current['reviewSources'])
        self.assertFalse(any(c['source'] == 'r18dev' for c in current['candidates']))
        plan = self.worker.preview(current, overwrite=False)
        accepted = self.worker.request('save.execute', {'planId': plan['planId']})['result']
        saved = self.worker.operation(accepted['operationId'])
        self.assertEqual('completed', saved['result']['outcome'])
        xml = video.with_suffix('.nfo').read_text()
        self.assertIn('IPX-082', xml); self.assertIn('NEW MOVIE B', xml); self.assertNotIn('OLD MOVIE A', xml); self.assertNotIn('OLD PLOT A', xml)
        self.assertEqual(original, video.read_bytes())

    def test_73_id_change_preserves_local_nfo_baseline_and_explicit_manual_review(self):
        xml = NFO.replace('DEMO-001', 'IPX-081')
        video, job = self.worker.import_video('IPX-081.mp4', nfo=xml)
        nfo = video.with_suffix('.nfo'); before = nfo.read_bytes(); original = video.read_bytes()
        self.configure([self.route(self.combined_url('ipx00081'), self.ok(self.r18('IPX-081', 'ipx00081', title='OLD ONLINE TITLE')))])
        self.search(job, 'r18dev'); job = self.worker.get_job(job['jobId'])
        job = self.worker.edit(job, {'title': 'Manual review for movie B', 'plot': ''})
        current = self.worker.edit(job, {'id': 'IPX-082'})
        self.assertFalse(current['localNfoSaveBlocked'])
        self.assertEqual(['local'], current['reviewSources'])
        self.assertEqual('Manual review for movie B', current['metadata']['title'])
        self.assertEqual('', current['metadata']['plot'])
        self.assertEqual(before, nfo.read_bytes())
        blocked = self.worker.preview(current, overwrite=False)
        self.assertFalse(blocked['canExecute']); self.assertEqual(before, nfo.read_bytes())
        current = self.worker.get_job(job['jobId']); plan = self.worker.preview(current, overwrite=True)
        accepted = self.worker.request('save.execute', {'planId': plan['planId']})['result']; done = self.worker.operation(accepted['operationId'])
        self.assertEqual('completed', done['result']['outcome'])
        written = nfo.read_text(); self.assertIn('IPX-082', written); self.assertIn('Manual review for movie B', written)
        self.assertNotIn('OLD ONLINE TITLE', written)
        self.assertEqual(original, video.read_bytes())
        # The original NFO's unknown nodes/attributes, actors and namespaced data remain round-trip inputs.
        old_root, new_root = ET.fromstring(before), ET.fromstring(nfo.read_bytes())
        for tag in ['custom', 'uniqueid', 'actor', 'set']:
            old_nodes, new_nodes = old_root.findall(tag), new_root.findall(tag)
            if tag != 'uniqueid': self.assertEqual([ET.tostring(e) for e in old_nodes], [ET.tostring(e) for e in new_nodes])

    def test_70_preview_cancel_releases_live_review_before_noncancelable_read_returns(self):
        video, job = self.worker.import_video('DEMO-001.mp4')
        nfo = video.with_suffix('.nfo'); before = (video.read_bytes(), nfo.read_bytes())
        self.configure(holdPreviewRead=True, ignorePreviewReadCancellation=True)
        started = self.worker.request('save.preview', {'jobId': job['jobId'],
            'expectedReviewRevision': job['reviewRevision'], 'async': True,
            'options': {'overwriteExisting': True, 'includeIdInTitle': False}})['result']
        operation = started['operationId']; self.wait_requests(kind='previewReadStarted')
        snapshot = self.worker.receive(self.worker.send('workspace.getSnapshot'), timeout=2)['result']
        self.assertTrue(snapshot['jobs'][0]['busy'])
        response = self.worker.receive(self.worker.send('operations.cancel', {'operationId': operation}), timeout=2)
        self.assertTrue(response['result']['received'])
        done = self.worker.operation(operation, timeout=2)
        self.assertEqual('canceled', done['status'], done)
        current = self.worker.get_job(job['jobId']); self.assertFalse(current['busy'])
        self.assertEqual(job['reviewRevision'], current['reviewRevision'], 'A canceled preview must not change save configuration or revision.')
        self.assertEqual(job['metadata'], current['metadata'])
        changed = self.worker.edit(current, {'title': 'Edit after canceled preview'})
        self.assertGreater(changed['reviewRevision'], current['reviewRevision'])
        blocked = self.worker.request('save.preview', {'jobId':changed['jobId'],
            'expectedReviewRevision':changed['reviewRevision'], 'async':True})['result']
        self.assertEqual('JOB_BUSY', self.worker.operation(blocked['operationId'])['error']['data']['reason'])
        self.assertEqual([], self.requests(kind='previewReadReturned'), 'The slow reader must still be gated at cancellation.')
        self.configure(holdPreviewRead=False)
        self.wait_requests(kind='previewReadReturned')
        self.assertEqual('canceled', self.worker.operation(operation)['status'])
        self.assertEqual(0, self.worker.request('workspace.getSnapshot')['result']['retention']['pendingPreviews'])
        self.assertEqual(changed, self.worker.get_job(changed['jobId']))
        stale = self.worker.request('save.preview', {'jobId':job['jobId'], 'expectedReviewRevision':job['reviewRevision'], 'async':True})['result']
        self.assertEqual('REVISION_CONFLICT', self.worker.operation(stale['operationId'])['error']['data']['reason'])
        self.assertEqual(before, (video.read_bytes(), nfo.read_bytes()))
        self.assertEqual([], list(self.worker.data.glob('recovery/*.json')), 'Canceled previews must not start a transaction.')
        preview = self.worker.preview(changed)
        self.assertTrue(preview['canExecute'])
        result = self.worker.request('save.execute', {'planId':preview['planId']})['result']
        self.assertEqual('completed', self.worker.operation(result['operationId'])['result']['outcome'])
        xml = ET.fromstring(nfo.read_bytes())
        self.assertEqual('Edit after canceled preview', xml.findtext('title'))
        self.assertEqual('KEEP UNKNOWN XML', xml.findtext('customNode/nested'))
        self.assertEqual(before[0], video.read_bytes())
        records = list(self.worker.data.glob('recovery/*.json'))
        self.assertEqual(1, len(records))
        self.assertEqual('completed', json.loads(records[0].read_text())['status'])

    def test_71_late_preview_read_failure_cannot_revive_a_canceled_plan_or_poison_retry(self):
        video, job = self.worker.import_video('DEMO-001.mp4')
        before = video.with_suffix('.nfo').read_bytes()
        self.configure(holdPreviewRead=True, ignorePreviewReadCancellation=True)
        started = self.worker.request('save.preview', {'jobId':job['jobId'], 'expectedReviewRevision':job['reviewRevision'], 'async':True})['result']
        operation = started['operationId']; self.wait_requests(kind='previewReadStarted')
        self.worker.request('operations.cancel', {'operationId':operation})
        self.assertEqual('canceled', self.worker.operation(operation, timeout=2)['status'])
        self.configure(holdPreviewRead=False, previewReadFailure=True)
        self.wait_requests(kind='previewReadReturned')
        self.configure(previewReadFailure=False)
        self.assertEqual('canceled', self.worker.operation(operation)['status'])
        current = self.worker.get_job(job['jobId'])
        preview = self.worker.preview(current, overwrite=False)
        self.assertFalse(preview['canExecute'], 'Cancellation never grants overwrite consent.')
        self.assertEqual(1, self.worker.request('workspace.getSnapshot')['result']['retention']['pendingPreviews'])
        self.assertEqual(before, video.with_suffix('.nfo').read_bytes())
        self.assertEqual([], list(self.worker.data.glob('recovery/*.json')))

    def test_68_first_manual_r18_target_resolves_without_applying_or_writing(self):
        video, job = self.worker.import_video('FNS-121.mp4', nfo=None)
        job = self.worker.edit(job, {'title': 'Manual title retained'})
        before = self.worker.get_job(job['jobId']); original = video.read_bytes()
        targets = self.worker.request('browser.getTargets', {'jobId':job['jobId']})['result']['targets']
        r18 = next(t for t in targets if t['source']=='r18dev')
        self.assertTrue(r18['requiresResolution']); self.assertEqual('https://r18.dev/',r18['url'])
        self.configure([self.route(self.combined_url('fns00121'), {'status':404}),
                        self.route(self.compact_url('FNS-121'),self.ok(self.r18('FNS-121','1fns121'))),
                        self.route(self.combined_url('1fns121'),self.ok(self.r18('FNS-121','1fns121')))])
        params={'jobId':job['jobId'],'expectedReviewRevision':job['reviewRevision'],'source':'r18dev'}
        response=self.worker.request('browser.resolveTarget',params)
        done=self.worker.operation(response['result']['operationId'])
        self.assertEqual('completed',done['status'],done)
        self.assertEqual('https://r18.dev/videos/vod/movies/detail/-/id=1fns121/',done['result']['url'])
        self.assertEqual(before,self.worker.get_job(job['jobId']))
        self.assertEqual(original,video.read_bytes());self.assertFalse(video.with_suffix('.nfo').exists())
        self.assertEqual(3,len(self.requests()))
        for source in ['libredmm','javlibrary']:
            response=self.worker.request('browser.resolveTarget',{**params,'source':source})
            self.assertEqual('completed',self.worker.operation(response['result']['operationId'])['status'])
        self.assertEqual(3,len(self.requests()),'Other browser targets must never query R18.')
        self.assertEqual('REVISION_CONFLICT',self.worker.request('browser.resolveTarget',{**params,'expectedReviewRevision':job['reviewRevision']-1})['error']['data']['reason'])

    def test_69_browser_resolution_cancellation_mismatch_and_stale_results_cannot_mutate_review(self):
        # This exercises ordering, not provider timeout (covered by test_44).
        # An explicit gate keeps the HTTP response pending until the edit commits;
        # an 80 ms delay raced the fixture's 150 ms timeout on loaded CI runners.
        config = Path(self.temp.name) / 'browser resolution.json'
        config.write_text(json.dumps({'providerTimeoutMilliseconds': 10000}))
        env = {**os.environ, 'JAVMETALITE_REGRESSION_CONFIG': str(config)}
        with Worker(Path(self.temp.name) / 'browser', executable=SEARCH_TEST_HOST, environment=env) as worker:
            video, job = worker.import_video('IPX-081.mp4', nfo=None)
            original = video.read_bytes()
            params = {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': 'r18dev'}
            url = self.combined_url('ipx00081')
            self.configure([self.route(url, {'status': 200, 'json': self.r18('IPX-081', 'ipx00081'),
                'waitForCancellation': True})], worker=worker)
            response = worker.request('browser.resolveTarget', params); operation = response['result']['operationId']
            self.wait_requests(worker=worker); worker.request('operations.cancel', {'operationId': operation})
            self.assertEqual('canceled', worker.operation(operation)['status'])
            self.assertEqual(job, worker.get_job(job['jobId']))
            self.configure([self.route(url, self.ok(self.r18('IPX-999', 'ipx00999')))], worker=worker)
            response = worker.request('browser.resolveTarget', params); done = worker.operation(response['result']['operationId'])
            self.assertEqual('PAGE_ID_MISMATCH', done['error']['data']['reason'], done)
            gate = Path(self.temp.name) / 'hold browser response'
            gate.touch()
            try:
                self.configure([self.route(url, {'status': 200, 'json': self.r18('IPX-081', 'ipx00081'),
                    'responseGateFile': str(gate)})], resetRequests=True, worker=worker)
                response = worker.request('browser.resolveTarget', params); operation = response['result']['operationId']
                self.wait_requests(kind='httpResponseHeld', worker=worker)
                self.assertEqual('running', worker.request('operations.get', {'operationId': operation})['result']['status'])
                changed = worker.edit(job, {'title': 'Newer edit stays'})
            finally:
                gate.unlink(missing_ok=True)
            done = worker.operation(operation)
            self.assertEqual('REVISION_CONFLICT', done['error']['data']['reason'], done)
            self.assertEqual(changed, worker.get_job(job['jobId']))
            self.assertEqual(original, video.read_bytes())
            self.assertFalse(video.with_suffix('.nfo').exists())
            self.assertEqual([], list(worker.data.glob('recovery/*.json')))

    def test_64_custom_profile_selects_each_fresh_field_and_retains_manual_candidates(self):
        _, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        job = self.worker.edit(job, {'title': 'Manual before fresh query'})
        self.configure([
            self.route(self.libre_url('IPX-081'), self.ok(self.libre('IPX-081', title='Fresh Libre title', makers=['Libre maker']))),
            self.route(self.combined_url('ipx00081'), self.ok(self.r18('IPX-081', 'ipx00081', title='Fresh R18 title', maker_name_en='R18 maker')))
        ])
        response = self.worker.request('jobs.search', {'jobId': job['jobId'], 'sourceMode': 'custom',
            'sourceProfile': {'titleSource': 'r18dev', 'originalTitleSource': 'libredmm', 'makerSource': 'libredmm'}})
        self.assertIn('result', response, response)
        result = self.worker.operation(response['result']['operationId'])
        self.assertEqual('completed', result['result']['outcome'], result)
        current = self.worker.get_job(job['jobId'])
        self.assertEqual('Fresh R18 title', current['metadata']['title'])
        self.assertEqual('Fresh Libre title', current['metadata']['originalTitle'])
        self.assertEqual('Libre maker', current['metadata']['maker'])
        self.assertGreater(current['reviewRevision'], job['reviewRevision'])
        self.assertTrue(any(c['field'] == 'title' and c['source'] == 'manual' and c['value'] == 'Manual before fresh query' for c in current['candidates']))
        current = self.worker.edit(current, {'title': 'Manual value remains recoverable'})
        self.configure([self.route(self.libre_url('IPX-081'), self.ok(self.libre('IPX-081', title='Latest Libre result')))])
        self.assertEqual('completed', self.search(current, 'libredmm')['result']['outcome'])
        latest = self.worker.get_job(job['jobId'])
        self.assertEqual('Latest Libre result', latest['metadata']['title'])
        self.assertTrue(any(c['field'] == 'title' and c['source'] == 'manual' and c['value'] == 'Manual value remains recoverable' for c in latest['candidates']))

    def test_65_invalid_custom_profile_rejects_before_queue_or_network_mutation(self):
        _, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        for profile in [None, [], {'titleSource': 'other'}, {'titleSource': 7}, {'unknownField': 'r18dev'}]:
            request = {'jobId': job['jobId'], 'sourceMode': 'custom', 'sourceProfile': profile}
            result = self.worker.request('jobs.search', request)
            self.assertEqual('INVALID_PARAMS', result['error']['data']['reason'], result)
            self.assertEqual(job, self.worker.get_job(job['jobId']))
        self.assertEqual([], self.requests())

    def test_66_cached_global_source_switch_is_atomic_offline_and_revision_checked(self):
        video, job = self.worker.import_video('IPX-081.mp4', nfo=NFO.replace('DEMO-001', 'IPX-081'))
        before = video.with_suffix('.nfo').read_bytes()
        local_baseline = job['metadata'].copy()
        self.configure([
            self.route(self.libre_url('IPX-081'), self.ok(self.libre('IPX-081', title='DMM full-source title', makers=['DMM maker']))),
            self.route(self.combined_url('ipx00081'), self.ok(self.r18('IPX-081', 'ipx00081', title='R18 full-source title', maker_name_en='R18 maker')))
        ])
        self.assertEqual('completed', self.search(job, 'all')['result']['outcome'])
        job = self.worker.get_job(job['jobId'])
        self.assertEqual({'local', 'libredmm', 'r18dev'}, set(job['reviewSources']))
        job = self.worker.edit(job, {'title': 'Manual candidate remains recoverable', 'director': 'Keep when missing'})
        plan = self.worker.preview(job)
        request_count = len(self.requests())
        job = self.worker.get_job(job['jobId'])
        original = job
        for source in ['unavailable', 'manual', 'all', 'javlibrary']:
            rejected = self.worker.request('jobs.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': source})
            self.assertEqual('INVALID_PARAMS', rejected['error']['data']['reason'])
            self.assertEqual(original, self.worker.get_job(job['jobId']))
        for source, title, maker in [('libredmm', 'DMM full-source title', 'DMM maker'), ('r18dev', 'R18 full-source title', 'R18 maker')]:
            previous = job
            job = self.worker.request('jobs.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': source})['result']
            self.assertEqual(title, job['metadata']['title']); self.assertEqual(maker, job['metadata']['maker'])
            self.assertEqual(source, job['reviewSource'], 'Retained missing fields must not hide the selected cached source')
            self.assertEqual(source, job['metadata']['sourceName'])
            self.assertEqual('Keep when missing', job['metadata']['director'])
            self.assertGreater(job['reviewRevision'], previous['reviewRevision'])
            stale = self.worker.request('jobs.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': previous['reviewRevision'], 'source': 'local'})
            self.assertEqual('REVISION_CONFLICT', stale['error']['data']['reason'])
            self.assertEqual(job, self.worker.get_job(job['jobId']))
            self.assertTrue(any(c['field'] == 'title' and c['source'] == 'manual' and c['value'] == 'Manual candidate remains recoverable' for c in job['candidates']))
        local = self.worker.request('jobs.selectSource', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': 'local'})['result']
        self.assertEqual(ET.fromstring(before).findtext('title'), local['metadata']['title'])
        self.assertEqual(local_baseline['sourceUrl'], local['metadata']['sourceUrl'])
        self.assertEqual(local_baseline['contentId'], local['metadata']['contentId'])
        self.assertEqual(request_count, len(self.requests()), 'Cached source selection must not issue any provider HTTP request')
        self.assertEqual('PLAN_STALE', self.worker.request('save.execute', {'planId': plan['planId']})['error']['data']['reason'])
        self.assertEqual(before, video.with_suffix('.nfo').read_bytes())
        self.assertEqual(b'SYNTHETIC VIDEO ONLY\0\1\2', video.read_bytes())

    def test_42_T04_only_selected_libre_batch_makes_zero_r18_requests(self):
        jobs = [self.worker.import_video(f'{movie}.mp4', nfo=None)[1] for movie in ('TEST-101', 'TEST-102', 'TEST-103')]
        self.configure([self.route(self.libre_url(job['metadata']['id']), self.ok(self.libre(job['metadata']['id']))) for job in jobs])
        started = self.worker.request('jobs.search', {'jobIds': [job['jobId'] for job in jobs], 'sourceMode': 'libredmm'})
        result = self.worker.operation(started['result']['operationId'])
        self.assertEqual('completed', result['result']['outcome'], result)
        self.assertEqual(['Completed'] * 3, [row['status'] for row in result['result']['jobResults']])
        self.assertEqual([self.libre_url(job['metadata']['id']) for job in jobs], [r['url'] for r in self.requests()])
        self.assertFalse(any('r18.dev' in request['url'] for request in self.requests()))
        for job in jobs:
            current = self.worker.get_job(job['jobId'])
            self.assertEqual(['libredmm'], [a['source'] for a in current['searchAttempts']])
            self.assertFalse(current['hasFailedSources'])
            self.assertEqual('unknown', current['coverQuality']['state'])
        # The distributed Worker cannot opt into fixture methods or a mock provider.
        with Worker(Path(self.temp.name) / 'production') as production:
            self.assertEqual('METHOD_NOT_FOUND', production.request('fixture.configure')['error']['data']['reason'])

    def test_43_T05_R07_partial_429_retry_only_failed_selected_source_preserves_review(self):
        _, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        libre_url, r18_url = self.libre_url('IPX-081'), self.combined_url('ipx00081')
        self.configure([self.route(libre_url, self.ok(self.libre('IPX-081'))),
                        self.route(r18_url, {'status': 429, 'retryAfterSeconds': 120})])
        done = self.search(job, 'all')
        self.assertEqual('partialFailure', done['result']['outcome'], done)
        self.assertEqual('partialFailure', done['result']['jobResults'][0]['outcome'])
        job = self.worker.get_job(job['jobId'])
        self.assertTrue(job['hasFailedSources']); self.assertTrue(job['hasPartialSourceFailure']); self.assertTrue(job['hasRateLimitedSource'])
        failure = next(a for a in job['searchAttempts'] if not a['success'])
        self.assertEqual('PROVIDER_RATE_LIMITED', failure['error']['reason']); self.assertIn('retryAt', failure['error'])
        self.assertEqual('Synthetic Libre title', job['metadata']['title'])
        job = self.worker.edit(job, {'title': 'Reviewed title must survive retry'})
        before_requests = self.requests()
        excluded = self.search(job, 'libredmm', retry=True)
        self.assertEqual('partialFailure', excluded['result']['outcome'])
        self.assertEqual(before_requests, self.requests(), 'Excluded failed R18 and already successful Libre must not be retried')
        self.configure([self.route(r18_url, self.ok(self.r18('IPX-081', 'ipx00081')))], advanceSeconds=121)
        retried = self.search(job, 'all', retry=True)
        self.assertEqual('completed', retried['result']['outcome'], retried)
        current = self.worker.get_job(job['jobId'])
        self.assertFalse(current['hasFailedSources'])
        self.assertEqual('Reviewed title must survive retry', current['metadata']['title'])
        self.assertEqual([libre_url, r18_url, r18_url], [r['url'] for r in self.requests()])
        self.assertEqual({'libredmm', 'r18dev', 'manual'}, {c['source'] for c in current['candidates'] if c['field'] == 'title'})

    def test_44_T05_timeout_not_found_and_G03_discovered_actual_content_id(self):
        _, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        guessed = self.combined_url('ipx00081')
        self.configure([self.route(guessed, {'status': 200, 'waitForCancellation': True})])
        failed = self.search(job, 'r18dev')
        self.assertEqual('Failed', failed['result']['jobResults'][0]['status'])
        current = self.worker.get_job(job['jobId'])
        self.assertEqual('PROVIDER_TIMEOUT', current['searchAttempts'][0]['error']['reason'])
        self.assertEqual('SearchFailed', current['searchState'], 'HTTP timeout is not a user cancellation')
        self.configure([self.route(guessed, {'status': 404}), self.route(self.compact_url('IPX-081'), {'status': 404})])
        absent = self.search(current, 'r18dev', retry=True)
        self.assertEqual('Failed', absent['result']['jobResults'][0]['status'])
        current = self.worker.get_job(job['jobId'])
        self.assertEqual('PROVIDER_NOT_FOUND', current['searchAttempts'][0]['error']['reason'])
        _, fns = self.worker.import_video('FNS-121.mp4', nfo=None)
        guessed_fns, compact, actual = self.combined_url('fns00121'), self.compact_url('FNS-121'), self.combined_url('1fns121')
        self.configure([self.route(guessed_fns, {'status': 404}),
            self.route(compact, self.ok({'content_id': '1fns121'})),
            self.route(actual, self.ok(self.r18('FNS-121', '1fns121', jacket_full_url='https://example.invalid/1fns121pl.jpg')))], resetRequests=True)
        resolved = self.search(fns, 'r18dev')
        self.assertEqual('completed', resolved['result']['outcome'], resolved)
        fns = self.worker.get_job(fns['jobId'])
        self.assertEqual('1fns121', fns['metadata']['contentId'])
        self.assertEqual([guessed_fns, compact, actual], [r['url'] for r in self.requests()])
        self.assertEqual('unknown', fns['coverQuality']['state'], 'Search must not eagerly download remote images')

    def test_45_R07_cancel_retains_review_and_fresh_query_promotes_latest_source(self):
        _, job = self.worker.import_video('IPX-081.mp4', nfo=None)
        libre_url, r18_url = self.libre_url('IPX-081'), self.combined_url('ipx00081')
        self.configure([self.route(libre_url, self.ok(self.libre('IPX-081')))])
        self.assertEqual('completed', self.search(job, 'libredmm')['result']['outcome'])
        job = self.worker.edit(self.worker.get_job(job['jobId']), {'title': 'Manual reviewed before canceled query'})
        self.configure([self.route(r18_url, {'status': 429})], holdScheduler=True)
        started = self.worker.request('jobs.search', {'jobId': job['jobId'], 'sourceMode': 'r18dev'})['result']['operationId']
        self.wait_requests(kind='schedulerWait')
        self.assertEqual('JOB_BUSY', self.worker.request('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'edits': {'title': 'late write'}})['error']['data']['reason'])
        self.assertEqual('JOB_BUSY', self.worker.request('jobs.remove', {'jobId': job['jobId']})['error']['data']['reason'])
        before = time.monotonic()
        self.worker.request('operations.cancel', {'operationId': started})
        stopped = self.worker.operation(started)
        self.assertLess(time.monotonic() - before, 1.5)
        self.assertEqual('canceled', stopped['result']['outcome'])
        self.assertEqual('Canceled', stopped['result']['jobResults'][0]['status'])
        current = self.worker.get_job(job['jobId'])
        self.assertEqual(job['metadata'], current['metadata']); self.assertEqual(job['reviewRevision'], current['reviewRevision'])
        self.assertFalse(current['busy'])
        self.configure([self.route(r18_url, self.ok(self.r18('IPX-081', 'ipx00081')))], advanceSeconds=121, holdScheduler=False)
        retried = self.search(current, 'r18dev')
        self.assertEqual('completed', retried['result']['outcome'], retried)
        current = self.worker.get_job(job['jobId'])
        self.assertEqual(self.r18('IPX-081', 'ipx00081')['title'], current['metadata']['title'])
        self.assertTrue(any(c['field'] == 'title' and c['source'] == 'manual' and c['value'] == 'Manual reviewed before canceled query' for c in current['candidates']))
        self.assertTrue(any(c['source'] == 'libredmm' for c in current['candidates']))
        self.assertEqual([libre_url, r18_url, r18_url], [r['url'] for r in self.requests()])

    def test_46_R07_cancel_queued_job_never_calls_provider_or_changes_other_job(self):
        jobs = [self.worker.import_video(f'TEST-{n}.mp4', nfo=None)[1] for n in (201, 202, 203)]
        self.configure([self.route(self.libre_url(j['metadata']['id']), {'status': 200, 'waitForCancellation': True}) for j in jobs])
        first = self.worker.request('jobs.search', {'jobId': jobs[0]['jobId']})['result']['operationId']
        second = self.worker.request('jobs.search', {'jobId': jobs[1]['jobId']})['result']['operationId']
        self.wait_requests(2)
        queued = self.worker.request('jobs.search', {'jobId': jobs[2]['jobId']})['result']['operationId']
        self.worker.request('operations.cancel', {'operationId': queued})
        self.assertEqual('canceled', self.worker.operation(queued)['result']['outcome'])
        for op in (first, second): self.worker.request('operations.cancel', {'operationId': op})
        for op in (first, second): self.worker.operation(op)
        self.assertEqual({self.libre_url(j['metadata']['id']) for j in jobs[:2]}, {r['url'] for r in self.requests()})
        for original in jobs:
            actual = self.worker.get_job(original['jobId'])
            self.assertEqual(original['metadata'], actual['metadata']); self.assertEqual(original['reviewRevision'], actual['reviewRevision'])
            self.assertFalse(actual['busy'])

    def test_48_P10_source_selection_is_atomic_and_does_not_download_hidden_samples(self):
        _, job = self.worker.import_video('TEST-920.mp4', nfo=None)
        shared_sample = 'https://awsimgsrc.dmm.com/dig/digital/video/test00920/test00920jp-1.jpg'
        libre_samples = [shared_sample, 'https://example.invalid/libre/sample2.jpg']
        self.configure([self.route(self.libre_url('TEST-920'), self.ok(self.libre('TEST-920',
            cover_image_url='https://example.invalid/libre/cover.jpg', thumbnail_image_url='https://example.invalid/libre/poster.jpg', sample_image_urls=libre_samples))),
            self.route(self.combined_url('test00920'), self.ok(self.r18('TEST-920', 'test00920',
                jacket_full_url='https://example.invalid/r18/cover.jpg', gallery=[{'image_full': 'https://example.invalid/r18/sample.jpg'}])))])
        self.assertEqual('completed', self.search(job, 'all')['result']['outcome'])
        job = self.worker.get_job(job['jobId'])
        self.assertEqual({'libredmm', 'r18dev'}, {a['source'] for a in job['artwork'] if a['kind'] == 'sample'})
        plan = self.worker.preview(job)
        job = self.worker.get_job(job['jobId'])
        request = {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'source': 'r18dev'}
        selected = self.worker.request('artwork.selectSource', request)['result']
        self.assertGreater(selected['reviewRevision'], job['reviewRevision'])
        self.assertEqual({'r18dev'}, {a['source'] for a in selected['artwork'] if a['selected'] and a['kind'] != 'sample'})
        self.assertEqual({'libredmm', 'r18dev'}, {a['source'] for a in selected['artwork'] if a['selected'] and a['kind'] == 'sample'},
            'A shared URL must remain visible in both source galleries, with consistent selection')
        self.assertEqual(3, len([a for a in selected['artwork'] if a['kind'] == 'sample']))
        self.assertEqual(len(selected['artwork']), len({a['artworkId'] for a in selected['artwork']}))
        self.assertEqual(1, len(selected['metadata']['screenshotUrls']))
        self.assertEqual('PLAN_STALE', self.worker.request('save.execute', {'planId': plan['planId']})['error']['data']['reason'])
        self.assertEqual('REVISION_CONFLICT', self.worker.request('artwork.selectSource', request)['error']['data']['reason'])
        self.assertEqual('ARTWORK_NOT_FOUND', self.worker.request('artwork.selectSource', {**request, 'expectedReviewRevision': selected['reviewRevision'], 'source': 'unregistered'})['error']['data']['reason'])
        self.assertEqual(selected, self.worker.get_job(job['jobId']), 'Rejected selections cannot partially change cover or samples')
        selected = self.worker.request('artwork.selectSource', {**request, 'expectedReviewRevision': selected['reviewRevision'], 'source': 'libredmm'})['result']
        self.assertEqual({'libredmm'}, {a['source'] for a in selected['artwork'] if a['selected'] and a['kind'] != 'sample'})
        self.assertEqual(2, len(selected['metadata']['screenshotUrls']))
        self.assertEqual(2, len(self.requests()), 'Source selection must not download any cover or sample')
        self.assertEqual({'.owner.json', '.owner.lock'}, {p.name for p in Path(self.worker.initialized['cacheRoot']).iterdir()})
        self.assertEqual(0, self.worker.request('workspace.getSnapshot')['result']['artworkCache']['bytes'])
        # A registered source with samples alone does not manufacture a cover candidate.
        video = Path(self.temp.name) / 'sample-only' / 'TEST-921.mp4'
        video.parent.mkdir(); video.write_bytes(b'SYNTHETIC VIDEO')
        local_samples = video.parent / 'extrafanart'; local_samples.mkdir()
        (local_samples / 'fanart1.png').write_bytes(synthetic_png(10, 10))
        sample_job = self.worker.get_job(self.worker.import_paths([video])['importedJobIds'][0])
        sample_job = self.worker.request('artwork.selectSource', {'jobId': sample_job['jobId'], 'expectedReviewRevision': sample_job['reviewRevision'], 'source': 'local'})['result']
        self.assertEqual(['sample'], [a['kind'] for a in sample_job['artwork']])
        self.assertTrue(sample_job['artwork'][0]['selected'])

    def test_50_T07_postcommit_cleanup_failure_keeps_completed_and_reports_actual_error(self):
        jobs = [self.worker.import_video(f'cleanup/TEST-{n}.mp4', nfo=None)[1] for n in (931, 932)]
        self.configure(cleanupFailure=True)
        preview = self.worker.request('save.preview', {'jobIds': [j['jobId'] for j in jobs],
            'expectedReviewRevisions': {j['jobId']: j['reviewRevision'] for j in jobs},
            'options': {'overwriteExisting': True}})['result']
        operation = self.worker.request('save.execute', {'planId': preview['planId']})['result']['operationId']
        done = self.worker.operation(operation)
        self.assertEqual('completed', done['status'])
        self.assertEqual('cleanupRequired', done['result']['outcome'], done)
        self.assertEqual(['Completed', 'NotStarted'], [r['status'] for r in done['result']['jobResults']])
        self.assertEqual([jobs[0]['jobId']], done['result']['savedJobIds'])
        self.assertEqual([jobs[1]['jobId']], done['result']['notStartedJobIds'])
        issues = done['result']['cleanupIssues']
        self.assertTrue(issues); self.assertEqual([issue['path'] for issue in issues], done['result']['cleanupWarnings'])
        self.assertTrue(all('Synthetic cleanup access denied' in issue['message'] for issue in issues))
        self.assertTrue(all(Path(issue['path']).is_dir() for issue in issues), 'Unconfirmed staging remains available for recovery')
        self.assertTrue(Path(jobs[0]['videoPaths'][0]).with_suffix('.nfo').exists())
        self.assertFalse(Path(jobs[1]['videoPaths'][0]).with_suffix('.nfo').exists())
        current = self.worker.get_job(jobs[0]['jobId'])
        self.assertEqual('Completed', current['saveState'])
        journals = [json.loads(path.read_text()) for path in (self.worker.data / 'recovery').glob('*.json')]
        self.assertEqual(['cleanupRequired'], [journal['status'] for journal in journals])
        self.assertTrue(journals[0]['cleanupIssues'][0]['error'])

    def test_51_T07_canceled_artwork_download_with_cleanup_failure_never_claims_safe_restore(self):
        received = threading.Event(); release = threading.Event()
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                self.send_response(200); self.send_header('Content-Type', 'image/png'); self.send_header('Content-Length', '4096'); self.end_headers()
                self.wfile.write(b'x'); self.wfile.flush(); received.set(); release.wait(5)
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler); server.daemon_threads = True
        thread = threading.Thread(target=lambda: server.serve_forever(poll_interval=.02), daemon=True); thread.start()
        try:
            video, job = self.worker.import_video()
            originals = {path: path.read_bytes() for path in (video, video.with_suffix('.nfo'))}
            html = (ROOT / 'fixtures/macos/javlibrary-detail.html').read_text().replace('https://example.invalid/syntheticpl.jpg', f'http://127.0.0.1:{server.server_port}/syntheticpl.jpg')
            imported = self.worker.request('browser.importPage', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
                'source': 'javlibrary', 'url': 'https://www.javlibrary.com/cn/?v=synthetic', 'html': html})
            job = self.worker.operation(imported['result']['operationId'])['result']['job']
            self.configure(cleanupFailure=True)
            plan = self.worker.preview(job, downloadFanart=True)
            operation = self.worker.request('save.execute', {'planId': plan['planId']})['result']['operationId']
            self.assertTrue(received.wait(3), 'Actual output HTTP body must be active before cancellation')
            self.worker.request('operations.cancel', {'operationId': operation})
            done = self.worker.operation(operation, timeout=3)
            self.assertEqual('completed', done['status'], done)
            self.assertEqual('recoveryRequired', done['result']['outcome'], done)
            self.assertEqual([], done['result']['savedJobIds'])
            self.assertEqual('Failed', done['result']['jobResults'][0]['status'])
            self.assertEqual('RECOVERY_REQUIRED', done['result']['jobResults'][0]['error']['data']['reason'])
            self.assertTrue(all('Synthetic cleanup access denied' in issue['message'] for issue in done['result']['cleanupIssues']))
            self.assertTrue(done['result']['cleanupIssues'])
            self.assertEqual(originals, {path: path.read_bytes() for path in originals})
            journals = [json.loads(path.read_text()) for path in (self.worker.data / 'recovery').glob('*.json')]
            self.assertEqual(['recoveryRequired'], [journal['status'] for journal in journals])
            self.assertTrue(journals[0]['cleanupIssues'][0]['error'])
        finally:
            release.set(); server.shutdown(); server.server_close(); thread.join(timeout=2)

    def test_53_R07_retry_without_failure_history_never_expands_provider_requests(self):
        _, job = self.worker.import_video('TEST-940.mp4', nfo=None)
        rejected = self.worker.request('jobs.retryFailedSources', {'jobId': job['jobId'], 'sourceMode': 'all'})
        self.assertEqual('INVALID_PARAMS', rejected['error']['data']['reason'])
        self.assertEqual([], self.requests()); self.assertEqual(job, self.worker.get_job(job['jobId']))
        self.configure([self.route(self.libre_url('TEST-940'), self.ok(self.libre('TEST-940')))])
        self.search(job, 'libredmm'); current = self.worker.get_job(job['jobId'])
        requests = self.requests()
        rejected = self.worker.request('jobs.retryFailedSources', {'jobId': job['jobId'], 'sourceMode': 'all'})
        self.assertEqual('INVALID_PARAMS', rejected['error']['data']['reason'])
        self.assertEqual(requests, self.requests()); self.assertEqual(current, self.worker.get_job(job['jobId']))


    def test_59_discovery_cancel_does_not_publish_jobs_or_hold_save_and_import_guards(self):
        video, original = self.worker.import_video('existing/TEST-601.mp4', nfo=None)
        original = self.worker.edit(original, {'title': 'Existing unsaved review'})
        plan = self.worker.preview(original)
        original = self.worker.get_job(original['jobId'])
        incoming = Path(self.temp.name) / 'TEST-602.mp4'; incoming.write_bytes(b'UNSELECTED INPUT')
        self.configure(holdDiscovery=True, resetRequests=True)
        started = self.worker.request('workspace.discoverInputs', {'paths': [str(incoming)]})['result']['operationId']
        self.wait_requests(kind='discoveryStarted')
        self.assertTrue(self.worker.request('operations.get', {'operationId': started})['result']['readOnly'])
        for method, params in [('workspace.discoverInputs', {'paths': [str(incoming)]}),
                               ('workspace.importInputs', {'paths': [str(incoming)]}), ('save.execute', {'planId': plan['planId']})]:
            rejected = self.worker.request(method, params)
            self.assertEqual('JOB_BUSY', rejected['error']['data']['reason'])
        self.worker.request('operations.cancel', {'operationId': started})
        self.assertEqual('canceled', self.worker.operation(started)['status'])
        self.assertEqual([original], self.worker.request('workspace.getSnapshot')['result']['jobs'])
        self.configure(holdDiscovery=False)
        discovery = self.worker.discover_paths([incoming])
        self.assertEqual(1, len(discovery['items']))
        self.worker.request('workspace.discardDiscovery', {'discoveryId': discovery['discoveryId']})
        self.assertFalse(video.with_suffix('.nfo').exists()); self.assertEqual(b'UNSELECTED INPUT', incoming.read_bytes())

    def test_60_X01_discovery_readfailed_substitute_retains_readable_inputs_and_tokens_expire(self):
        readable = Path(self.temp.name) / 'TEST-611.mp4'; readable.write_bytes(b'READABLE')
        failing = Path(self.temp.name) / 'unreadable branch'; failing.mkdir()
        self.configure(discoveryReadFailures=[str(failing)])
        discovery = self.worker.discover_paths([readable, failing])
        self.assertEqual(1, len(discovery['items']))
        self.assertEqual('TEST-611', discovery['items'][0]['movieId'])
        self.assertEqual([{'path': str(failing), 'message': 'Synthetic ordinary filesystem I/O error (EIO).', 'kind': 'ReadFailed'}], discovery['diagnostics'])
        self.assertEqual([], self.worker.request('workspace.getSnapshot')['result']['jobs'])
        self.configure(advanceSeconds=901)
        rejected = self.worker.request('workspace.importDiscovery', {'discoveryId': discovery['discoveryId'], 'selectedItemIds': [discovery['items'][0]['itemId']]})
        self.assertEqual('DISCOVERY_STALE', rejected['error']['data']['reason'])
        fresh = self.worker.discover_paths([readable, failing])
        done = self.worker.confirm_discovery(fresh, [fresh['items'][0]['itemId']])
        self.assertEqual('completed', done['status']); self.assertEqual(1, len(done['result']['importedJobIds']))
        self.assertEqual('ReadFailed', done['result']['diagnostics'][0]['kind'])
        self.assertEqual(b'READABLE', readable.read_bytes())

    def test_61_confirmed_import_cancellation_is_atomic_after_local_loading_started(self):
        _, original = self.worker.import_video('existing/TEST-620.mp4', nfo=None)
        original = self.worker.edit(original, {'title': 'Unsaved review survives canceled confirmation'})
        incoming = Path(self.temp.name) / 'large selected set'; incoming.mkdir()
        for index in range(300):
            folder = incoming / f'item-{index:04}'; folder.mkdir()
            (folder / f'TEST-{7000 + index}.mp4').write_bytes(b'SYNTHETIC MEDIA')
            (folder / 'movie.nfo').write_text(f'<movie><id>TEST-{7000 + index}</id><title>Synthetic</title><plot>{"known bytes " * 100}</plot><unknown>KEEP</unknown></movie>')
        discovery = self.worker.discover_paths([incoming])
        self.assertEqual(300, len(discovery['items']))
        params = {'discoveryId': discovery['discoveryId'], 'selectedItemIds': [i['itemId'] for i in discovery['items']]}
        operation = self.worker.request('workspace.importDiscovery', params)['result']['operationId']
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            status = self.worker.request('operations.get', {'operationId': operation})['result']
            if status['phase'] == 'loadingLocal': break
            self.assertEqual('running', status['status'], 'Confirmation finished before cancellation could exercise loading')
        self.assertEqual('loadingLocal', status['phase'])
        rejected = self.worker.request('jobs.remove', {'jobId': original['jobId']})
        self.assertEqual('JOB_BUSY', rejected['error']['data']['reason'])
        self.worker.request('operations.cancel', {'operationId': operation})
        self.assertEqual('canceled', self.worker.operation(operation)['status'])
        self.assertEqual([original], self.worker.request('workspace.getSnapshot')['result']['jobs'])
        self.assertEqual(300, len(list(incoming.rglob('*.mp4'))))
        rejected = self.worker.request('workspace.importDiscovery', params)
        self.assertEqual('DISCOVERY_STALE', rejected['error']['data']['reason'])


    def test_62_regression_host_startup_config_and_environment_are_isolated_from_production(self):
        root = Path(self.temp.name) / 'QA startup'; root.mkdir()
        config = root / 'fixed routes.json'
        config.write_text(json.dumps({'providerTimeoutMilliseconds': 1500, 'routes': [self.route(self.libre_url('TEST-630'),
            {'status': 200, 'delayMilliseconds': 250, 'json': self.libre('TEST-630')})]}))
        for cli in (False, True):
            directory = root / ('cli' if cli else 'environment')
            data = directory / 'explicit data'
            env = {**os.environ, 'JAVMETALITE_REGRESSION_DATA_ROOT': str(data), 'JAVMETALITE_REGRESSION_CONFIG': str(config)}
            arguments = []
            if cli:
                env['JAVMETALITE_REGRESSION_DATA_ROOT'] = 'relative env ignored'
                env['JAVMETALITE_REGRESSION_CONFIG'] = str(root / 'missing env config.json')
                arguments = ['--data-root', str(data), '--fixture-config', str(config)]
            with Worker(directory, executable=SEARCH_TEST_HOST, arguments=arguments, environment=env) as fixture_worker:
                self.assertEqual(str(data), fixture_worker.request('preferences.get')['result']['dataRoot'])
                _, job = fixture_worker.import_video('TEST-630.mp4', nfo=None)
                started = fixture_worker.request('jobs.search', {'jobId': job['jobId'], 'sourceMode': 'libredmm'})
                done = fixture_worker.operation(started['result']['operationId'])
                self.assertEqual('completed', done['result']['outcome'], '250ms route must succeed with configured timeout instead of default150ms')
                records = fixture_worker.request('fixture.requests')['result']['requests']
                self.assertEqual([self.libre_url('TEST-630')], [r['url'] for r in records if r['kind'] == 'http'])
        # Production ignores the host-only environment variables and fixture method.
        env = {**os.environ, 'JAVMETALITE_REGRESSION_DATA_ROOT': str(root / 'forbidden data'), 'JAVMETALITE_REGRESSION_CONFIG': str(config)}
        with Worker(root / 'production', environment=env) as production:
            self.assertEqual(str(production.data), production.request('preferences.get')['result']['dataRoot'])
            self.assertEqual('METHOD_NOT_FOUND', production.request('fixture.configure', {})['error']['data']['reason'])
            self.assertFalse((root / 'forbidden data').exists())

    def test_63_regression_host_async_artwork_batch_preview_outlives_request_document(self):
        media = Path(self.temp.name) / 'async batch media'; media.mkdir()
        videos = []
        for number in (641, 642):
            folder = media / f'TEST-{number}'; folder.mkdir()
            video = folder / f'TEST-{number}.mp4'; video.write_bytes(b'SYNTHETIC VIDEO ONLY')
            video.with_suffix('.nfo').write_text(
                f'<movie custom="keep"><id>TEST-{number}</id><title>Synthetic batch review</title>'
                '<unknown>KEEP UNKNOWN XML</unknown><resume><position>1</position></resume></movie>')
            (folder / 'poster.png').write_bytes(synthetic_png(20, 30))
            (folder / 'fanart.png').write_bytes(synthetic_png(60, 40))
            samples = folder / 'extrafanart'; samples.mkdir()
            (samples / 'sample001.png').write_bytes(synthetic_png(12, 8))
            videos.append(video)
        def inventory():
            return {str(path.relative_to(media)): hashlib.sha256(path.read_bytes()).hexdigest()
                    for path in media.rglob('*') if path.is_file()}
        original = inventory()
        job_ids = self.worker.import_paths(videos)['importedJobIds']
        self.assertEqual(2, len(job_ids))
        # Exercise repeated native-shaped async requests. A following RPC crosses
        # the host's original JsonDocument disposal boundary before result polling.
        for attempt in range(8):
            with self.subTest(attempt=attempt):
                jobs = [self.worker.get_job(job_id) for job_id in job_ids]
                started = self.worker.request('save.preview', {'jobIds': job_ids,
                    'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in jobs},
                    'async': True, 'options': {'writeNfo': True, 'downloadPoster': True,
                        'downloadFanart': True, 'downloadExtrafanart': True,
                        'replaceLocalExtrafanart': True, 'overwriteExisting': True,
                        'includeIdInTitle': False, 'targetMode': 'VideoDirectory',
                        'renameVideo': False, 'customRootDirectory': ''}})
                self.assertIn('result', started, started)
                self.assertIn('result', self.worker.request('workspace.getSnapshot'))
                done = self.worker.operation(started['result']['operationId'])
                self.assertEqual('completed', done['status'], done)
                preview = done['result']
                self.assertTrue(preview['canExecute'], preview)
                self.assertEqual(job_ids, [job['jobId'] for job in preview['jobs']])
                self.assertTrue(preview['planId'])
                self.assertEqual(original, inventory(), 'Preview must not write or remove any media file')
        self.assertEqual([], self.requests(), 'Local artwork planning must not contact a provider')
        self.assertEqual([], list((self.worker.data / 'recovery').glob('*.json')),
                         'Preview must not begin a file transaction')


if __name__ == '__main__':
    print('Protocol scope: production only' if PRODUCTION_ONLY else 'Protocol scope: production + fixed-HTTP Workspace/provider integration', file=sys.stderr)
    unittest.main(verbosity=2, defaultTest='WorkerTests' if PRODUCTION_ONLY else None)
