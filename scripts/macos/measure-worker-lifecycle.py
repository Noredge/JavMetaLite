#!/usr/bin/env python3
"""Bounded synthetic Worker lifecycle/RSS measurement; never drives native UI."""
import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import platform
import queue
import struct
import subprocess
import tempfile
import threading
import time
import zlib


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def rss(pid):
    result = subprocess.run(['/bin/ps', '-o', 'rss=', '-p', str(pid)], capture_output=True, text=True)
    return int(result.stdout.strip()) * 1024 if result.returncode == 0 and result.stdout.strip() else None


def png():
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 600, 400, 8, 2, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress((b'\0' + bytes((20, 80, 140)) * 600) * 400)) + chunk(b'IEND', b''))


class Worker:
    """Discard notifications as they arrive: the measurement driver must not retain history."""
    def __init__(self, executable, data):
        self.proc = subprocess.Popen([str(executable), '--data-root', str(data)], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        self.messages = queue.Queue()
        self.stderr_tail = deque(maxlen=12)
        self.sequence = 0
        self.notifications = 0
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.err = threading.Thread(target=self.read_errors, daemon=True)
        self.reader.start(); self.err.start()
        self.hello = self.call('initialize', {'protocolVersion': 1})

    def read(self):
        try:
            for line in self.proc.stdout:
                message = json.loads(line)
                if 'method' in message:
                    self.notifications += 1
                else:
                    self.messages.put(message)
        except Exception as error:
            self.messages.put(error)
        finally:
            self.messages.put(EOFError('Worker stdout ended'))

    def read_errors(self):
        for line in self.proc.stderr:
            self.stderr_tail.append(line.decode(errors='replace').strip())

    def call(self, method, params=None):
        self.sequence += 1
        request_id = str(self.sequence)
        frame = {'jsonrpc': '2.0', 'id': request_id, 'method': method, 'params': params or {}}
        self.proc.stdin.write((json.dumps(frame) + '\n').encode()); self.proc.stdin.flush()
        response = self.messages.get(timeout=120)
        if isinstance(response, Exception):
            raise response
        assert response.get('id') == request_id, response
        assert 'result' in response, response
        return response['result']

    def operation(self, operation_id):
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            result = self.call('operations.get', {'operationId': operation_id})
            if result['status'] != 'running':
                return result
            time.sleep(.005)
        raise TimeoutError(operation_id)

    def close(self):
        try:
            self.call('shutdown')
            self.proc.stdin.close()
            self.proc.wait(timeout=20)
            assert self.proc.returncode == 0, self.proc.returncode
        finally:
            if self.proc.poll() is None:
                self.proc.kill(); self.proc.wait(timeout=5)
            self.reader.join(timeout=2); self.err.join(timeout=2)
            for pipe in (self.proc.stdin, self.proc.stdout, self.proc.stderr):
                pipe.close()


def timed(action):
    start = time.monotonic()
    result = action()
    return result, round((time.monotonic() - start) * 1000, 3)


def measure(args, report):
    with tempfile.TemporaryDirectory(prefix='JavMetaLite lifecycle 合成 ') as directory:
        root = Path(directory)
        media = root / 'media'
        for index in range(args.jobs):
            movie = media / f'DEMO-{10000 + index}'
            movie.mkdir(parents=True)
            (movie / f'DEMO-{10000 + index}.mp4').write_bytes(b'SYNTHETIC NONPLAYABLE LIFECYCLE FIXTURE')
        (media / 'DEMO-10000' / 'fanart.png').write_bytes(png())
        originals = {str(path.relative_to(media)): sha(path) for path in media.rglob('*') if path.is_file()}
        report['syntheticFiles'] = len(originals)
        report['syntheticBytes'] = sum(path.stat().st_size for path in media.rglob('*') if path.is_file())
        report['coldProcesses'] = []

        def import_all(worker):
            started = worker.call('workspace.importInputs', {'paths': [str(media)], 'recursive': True})
            done = worker.operation(started['operationId'])
            assert done['status'] == 'completed', done
            return done['result']

        def image_preview(worker, snapshot):
            job = next(job for job in snapshot['jobs'] if any(a['artworkId'] == 'local-fanart' for a in job['artwork']))
            return worker.call('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': 'local-fanart'})

        for index in range(args.cold_runs):
            worker, startup = timed(lambda: Worker(args.worker, root / f'cold-{index}'))
            try:
                imported, import_ms = timed(lambda: import_all(worker))
                assert len(imported['importedJobIds']) == args.jobs
                snapshot = worker.call('workspace.getSnapshot')
                first, first_ms = timed(lambda: image_preview(worker, snapshot))
                second, second_ms = timed(lambda: image_preview(worker, snapshot))
                assert first['cacheId'] == second['cacheId']
                report['coldProcesses'].append({'initializeMs': startup, 'importMs': import_ms,
                    'firstLocalPreviewMs': first_ms, 'repeatLocalPreviewMs': second_ms,
                    'rssBytes': rss(worker.proc.pid)})
                cache = Path(worker.hello['cacheRoot'])
            finally:
                worker.close()
            assert not cache.exists(), 'Session cache remains after graceful shutdown'

        worker = Worker(args.worker, root / 'warm')
        report['workerVersion'] = worker.hello['workerVersion']
        report['pid'] = worker.proc.pid
        cache = Path(worker.hello['cacheRoot'])
        report['cycles'] = []
        start = time.monotonic()
        try:
            while len(report['cycles']) < args.cycles or time.monotonic() - start < args.duration_seconds:
                if len(report['cycles']) >= args.maximum_cycles:
                    raise RuntimeError('maximum-cycles reached before requested measurement duration')
                imported, import_ms = timed(lambda: import_all(worker))
                assert len(imported['importedJobIds']) == args.jobs
                duplicate = import_all(worker)
                assert duplicate['importedJobIds'] == [], duplicate
                snapshot = worker.call('workspace.getSnapshot')
                assert {job['jobId'] for job in snapshot['jobs']} == set(imported['importedJobIds'])
                first, first_ms = timed(lambda: image_preview(worker, snapshot))
                second, repeat_ms = timed(lambda: image_preview(worker, snapshot))
                assert first['cacheId'] == second['cacheId'] and first['width'] == 600 and first['height'] == 400
                job = snapshot['jobs'][0]
                preview_params = {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'], 'async': True}
                pending = worker.call('save.preview', preview_params)
                worker.call('operations.cancel', pending)
                canceled = worker.operation(pending['operationId'])
                assert canceled['status'] in ('completed', 'canceled'), canceled
                job = worker.call('jobs.get', {'jobId': job['jobId']})
                preview_params['expectedReviewRevision'] = job['reviewRevision']
                pending = worker.call('save.preview', preview_params)
                done = worker.operation(pending['operationId'])
                assert done['status'] == 'completed' and done['result']['canExecute'], done
                for item in snapshot['jobs']:
                    worker.call('jobs.remove', {'jobId': item['jobId']})
                empty = worker.call('workspace.getSnapshot')
                assert empty['jobs'] == [] and empty['activeOperations'] == [], empty
                assert originals == {str(path.relative_to(media)): sha(path) for path in media.rglob('*') if path.is_file()}
                record = {'index': len(report['cycles']) + 1, 'elapsedSeconds': round(time.monotonic() - start, 3),
                    'importMs': import_ms, 'firstLocalPreviewMs': first_ms, 'repeatLocalPreviewMs': repeat_ms,
                    'cancelRaceTerminalStatus': canceled['status'], 'rssBytes': rss(worker.proc.pid),
                    'cacheFiles': sum(path.is_file() for path in cache.rglob('*')),
                    'cacheBytes': sum(path.stat().st_size for path in cache.rglob('*') if path.is_file()),
                    'retention': empty.get('retention')}
                report['cycles'].append(record)
                if len(report['cycles']) % 25 == 0:
                    print(json.dumps({'cycle': len(report['cycles']), 'elapsedSeconds': record['elapsedSeconds'],
                                      'rssBytes': record['rssBytes']}), flush=True)
                if args.interval_seconds:
                    time.sleep(args.interval_seconds)
            report['measuredSeconds'] = round(time.monotonic() - start, 3)
            report['mediaUnchangedEveryCycle'] = True
            report['emptyQueueAfterEveryCycle'] = True
            report['notificationsDiscardedByDriver'] = worker.notifications
        finally:
            worker.close()
        report['cacheRemovedAfterShutdown'] = not cache.exists()
        assert report['cacheRemovedAfterShutdown']
        values = [item['rssBytes'] for item in report['cycles'] if item['rssBytes'] is not None]
        report['rssSummaryBytes'] = {'first': values[0], 'last': values[-1], 'minimum': min(values),
                                     'maximum': max(values), 'lastMinusFirst': values[-1] - values[0]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('worker', type=Path, nargs='?')
    parser.add_argument('--label', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--jobs', type=int, default=16)
    parser.add_argument('--cycles', type=int, default=100)
    parser.add_argument('--maximum-cycles', type=int, default=2000)
    parser.add_argument('--duration-seconds', type=float, default=0)
    parser.add_argument('--interval-seconds', type=float, default=1)
    parser.add_argument('--cold-runs', type=int, default=3)
    parser.add_argument('--observe-pid', type=int, action='append', default=[])
    args = parser.parse_args()
    if args.output.exists():
        parser.error('output exists; use a new evidence path')
    if not 1 <= args.jobs <= 1000 or not 1 <= args.cycles <= args.maximum_cycles <= 10000:
        parser.error('jobs 1..1000 and 1 <= cycles <= maximum-cycles <= 10000 required')
    if min(args.duration_seconds, args.interval_seconds, args.cold_runs) < 0:
        parser.error('negative timing/count is invalid')
    if not args.observe_pid and (args.worker is None or not args.worker.is_file()):
        parser.error('an existing Worker executable is required')
    report = {'label': args.label, 'scope': 'Bounded local measurement; RSS includes runtime/native allocations, not a leak diagnosis or long-term/UI acceptance.',
              'coldDefinition': 'Fresh Worker process and empty session cache; OS filesystem cache is NOT purged.',
              'host': platform.platform(), 'architecture': platform.machine(), 'scriptSha256': sha(__file__),
              'startedUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'parameters': vars(args).copy()}
    report['parameters'] = {k: str(v) if isinstance(v, Path) else v for k, v in report['parameters'].items()}
    try:
        if args.observe_pid:
            report['scope'] = 'Read-only process RSS sampling; all UI actions are performed separately by the human/root operator.'
            report['samples'] = []
            start = time.monotonic()
            while time.monotonic() - start < args.duration_seconds:
                report['samples'].append({'elapsedSeconds': round(time.monotonic() - start, 3),
                                          'rssBytesByPid': {str(pid): rss(pid) for pid in args.observe_pid}})
                time.sleep(max(.1, args.interval_seconds))
        else:
            args.worker = args.worker.resolve()
            report['workerPath'] = str(args.worker)
            report['workerExecutableSha256'] = sha(args.worker)
            report['workerAssemblySha256'] = sha(args.worker.parent / (args.worker.name + '.dll'))
            for parent in args.worker.parents:
                manifest = parent / 'Resources' / 'BUILD-MANIFEST.json'
                if manifest.is_file():
                    report['buildManifest'] = json.loads(manifest.read_text())
                    break
            measure(args, report)
        report['completed'] = True
    except Exception as error:
        report['completed'] = False
        report['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')
        print(json.dumps({key: report.get(key) for key in ('label', 'completed', 'measuredSeconds', 'rssSummaryBytes', 'error')}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
