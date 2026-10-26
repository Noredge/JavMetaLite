#!/usr/bin/env python3
"""Reproducible local Worker timings; never claims native UI/online performance."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import tempfile
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('worker', type=Path)
parser.add_argument('--count', type=int, default=1000)
parser.add_argument('--output', type=Path)
args = parser.parse_args()
if not 1 <= args.count <= 1000:
    parser.error('count must be between 1 and 1000')
os.environ['JAVMETALITE_TEST_WORKER'] = str(args.worker.resolve())
# Reuse the tested pipe/framing harness. It reads argv only at module initialization.
saved_argv = sys.argv
sys.argv = [sys.argv[0]]
spec = importlib.util.spec_from_file_location('protocol_harness', Path(__file__).with_name('test_worker.py'))
harness = importlib.util.module_from_spec(spec)
spec.loader.exec_module(harness)
sys.argv = saved_argv

def host_hardware():
    if platform.system() != 'Darwin':
        return {'processor': platform.processor()}
    return {
        key: subprocess.check_output(['/usr/sbin/sysctl', '-n', name], text=True).strip()
        for key, name in [('model', 'hw.model'), ('memoryBytes', 'hw.memsize'), ('processor', 'machdep.cpu.brand_string')]
    }

def timed(action):
    start = time.perf_counter()
    value = action()
    return value, (time.perf_counter() - start) * 1000

def require_result(response):
    assert 'result' in response, response
    return response['result']

with tempfile.TemporaryDirectory(prefix='JavMetaLite performance 合成 ') as temporary:
    root = Path(temporary)
    media = root / 'synthetic'
    media.mkdir()
    for index in range(args.count):
        identifier = f'DEMO-{10000 + index}'
        (media / f'{identifier}.mp4').write_bytes(b'SYNTHETIC NONPLAYABLE PERFORMANCE FIXTURE')
    worker, startup_ms = timed(lambda: harness.Worker(root))
    try:
        def import_all():
            start = require_result(worker.request('workspace.importInputs', {'paths': [str(media)], 'recursive': True}))
            done = worker.operation(start['operationId'], timeout=180)
            assert done['status'] == 'completed', done
            assert len(done['result']['importedJobIds']) == args.count, done
            assert not done['result']['warnings'], done
            return done
        _, import_ms = timed(import_all)
        latencies = []
        for _ in range(10):
            snapshot, elapsed = timed(lambda: require_result(worker.request('workspace.getSnapshot')))
            assert len(snapshot['jobs']) == args.count
            latencies.append(elapsed)
        job = snapshot['jobs'][args.count // 2]
        edited, edit_ms = timed(lambda: require_result(worker.request('jobs.applyEdits', {
            'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
            'edits': {'title': 'Synthetic measured edit 測試 計測'}})))
        def preview_one():
            started = require_result(worker.request('save.preview', {
                'jobId': job['jobId'], 'expectedReviewRevision': edited['reviewRevision'],
                'async': True, 'options': {'writeNfo': True}}))
            done = worker.operation(started['operationId'], timeout=180)
            assert done['status'] == 'completed' and done['result']['canExecute'], done
            return done
        _, preview_ms = timed(preview_one)
        assert len(list(media.iterdir())) == args.count, 'Preview wrote media files'
        report = {
            'scope': 'Real local Worker only; synthetic tiny files; no native UI rendering or online timing claim',
            'host': platform.platform(), 'architecture': platform.machine(), 'hardware': host_hardware(),
            'workerVersion': worker.initialized['workerVersion'], 'syntheticJobs': args.count,
            'spawnAndInitializeMs': round(startup_ms, 2), 'importMs': round(import_ms, 2),
            'snapshotRoundTripMedianMs': round(statistics.median(latencies), 2),
            'snapshotRoundTripMaxMs': round(max(latencies), 2),
            'editRoundTripMs': round(edit_ms, 2), 'singlePreviewWithWholeQueueProtectionMs': round(preview_ms, 2),
            'snapshotJsonBytes': len(json.dumps(snapshot, ensure_ascii=False).encode()),
            'noMediaWritesBeforeConfirmation': True,
        }
    finally:
        worker.close()
if args.output:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')
print(json.dumps(report, indent=2, ensure_ascii=False))
