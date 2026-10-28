#!/usr/bin/env python3
"""Bounded parallel Worker suites, with isolated temporary roots and durable logs."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import os
import signal
from pathlib import Path
import subprocess
import sys
import tempfile
import time

HERE = Path(__file__).resolve().parent


def run_suites(suites, log_dir, jobs=2, environment=None, timeout=600, profile='full'):
    if profile not in ('core', 'full'):
        raise ValueError('Unknown suite profile: ' + profile)
    log_dir = Path(log_dir).resolve()
    log_dir.mkdir(parents=True, exist_ok=True)
    base_env = dict(os.environ if environment is None else environment)
    started = time.monotonic()

    def execute(suite):
        name, command = suite
        began = time.monotonic()
        print(f'SUITE START {name}', flush=True)
        log = log_dir / (name + '.log')
        # Keep fixture paths short: retention cases already exercise long legal
        # paths. A checkout nested inside the log path can exceed macOS PATH_MAX.
        short_root = '/tmp' if os.name == 'posix' else None
        with tempfile.TemporaryDirectory(prefix='jml-' + name + '-', dir=short_root) as temp:
            env = {**base_env, 'TMPDIR': temp, 'JAVMETALITE_TEST_PROFILE': profile,
                   'JAVMETALITE_TEST_TIMINGS': str(log_dir / (name + '.timings.json'))}
            with log.open('w') as stream:
                try:
                    child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                             env=env, start_new_session=True)
                    try:
                        code = child.wait(timeout=timeout)
                    except subprocess.TimeoutExpired:
                        # The Python driver can own Worker children. Kill the entire
                        # disposable suite group rather than leaving orphan engines.
                        os.killpg(child.pid, signal.SIGKILL)
                        child.wait()
                        stream.write(f'Suite exceeded {timeout} seconds; process group terminated.\n')
                        code = 124
                except OSError as error:
                    stream.write(f'Runner failure: {error}\n')
                    code = 124
        row = {'suite': name, 'exitCode': code, 'seconds': round(time.monotonic() - began, 3),
               'log': str(log)}
        print(f'SUITE END {name} exit={code} seconds={row["seconds"]}', flush=True)
        content = log.read_text(errors='replace')
        print(content if code else '\n'.join(content.splitlines()[-8:]), flush=True)
        return row

    # Collect every result; a failed suite must not skip another safety suite.
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = [pool.submit(execute, suite) for suite in suites]
        rows = [future.result() for future in as_completed(futures)]
    rows.sort(key=lambda row: next(i for i, s in enumerate(suites) if s[0] == row['suite']))
    report = {'profile': profile, 'jobs': jobs, 'seconds': round(time.monotonic() - started, 3), 'suites': rows}
    (log_dir / 'worker-suites.json').write_text(json.dumps(report, indent=2) + '\n')
    with (log_dir / 'results.tsv').open('a') as stream:
        for row in rows:
            stream.write(f'{row["suite"]}\t{row["exitCode"]}\t{row["seconds"]}\n')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--mode', choices=['development', 'package'], required=True)
    parser.add_argument('--profile', choices=['core', 'full'], default='full')
    parser.add_argument('--log-dir', type=Path, required=True)
    parser.add_argument('--jobs', type=int, choices=[1, 2, 3, 4], default=2)
    args = parser.parse_args()
    if args.mode == 'package' and args.profile != 'full':
        parser.error('Final package verification requires the full profile')
    worker = str(args.worker.resolve())
    protocol_flags = ['--require-search-host'] if args.mode == 'development' else ['--production-only']
    suites = [(name, [sys.executable, str(HERE / script), worker, *flags]) for name, script, flags in [
        ('protocol', 'test_worker.py', protocol_flags),
        ('retention', 'test_worker_retention.py', []),
        ('queue-interactions', 'test_queue_interactions.py', []),
        # Package verification runs shipping cache cases once, after extraction.
        *([('artwork-cache', 'test_worker_artwork_cache.py', ['--require-search-host'])]
          if args.mode == 'development' else [])]]
    report = run_suites(suites, args.log_dir, args.jobs, profile=args.profile)
    raise SystemExit(1 if any(row['exitCode'] for row in report['suites']) else 0)


if __name__ == '__main__':
    main()
