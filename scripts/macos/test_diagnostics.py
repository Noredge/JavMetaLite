"""Per-case and RPC phase timings for disposable Worker regression drivers."""
import json
import os
from pathlib import Path
import sys
import time
import unittest


def phase(name, started, **details):
    elapsed = time.monotonic() - started
    if elapsed >= 1 or details.get('error') or details.get('forcedKill'):
        print('WORKER_PHASE ' + json.dumps({'phase': name, 'seconds': round(elapsed, 3), **details}),
              file=sys.stderr, flush=True)


def stop_http_server(server, thread):
    started = time.monotonic()
    server.shutdown()
    phase('http.shutdown', started)
    started = time.monotonic()
    server.server_close()
    thread.join(timeout=2)
    phase('http.close', started)


class TimedResult(unittest.TextTestResult):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.timings = []

    def startTest(self, test):
        self.started = time.monotonic()
        super().startTest(test)

    def stopTest(self, test):
        elapsed = time.monotonic() - self.started
        self.timings.append({'test': test.id(), 'seconds': round(elapsed, 3)})
        if elapsed >= 1:
            self.stream.writeln(f'TEST_TIME {test.id()} {elapsed:.3f}s')
        super().stopTest(test)


class TimedRunner(unittest.TextTestRunner):
    resultclass = TimedResult

    def run(self, test):
        result = super().run(test)
        destination = os.environ.get('JAVMETALITE_TEST_TIMINGS')
        if destination:
            Path(destination).write_text(json.dumps({'passed': result.wasSuccessful(),
                'testsRun': result.testsRun, 'skipped': len(result.skipped),
                'tests': result.timings}, indent=2) + '\n')
        return result
