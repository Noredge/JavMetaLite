"""Fast, deterministic checks for cancellation recovery in the measurement driver."""
import copy
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).with_name('measure-worker-lifecycle.py')
SPEC = importlib.util.spec_from_file_location('worker_lifecycle_measurement', SCRIPT)
measurement = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(measurement)
BUSY = {'status': 'failed', 'error': {'data': {'reason': 'JOB_BUSY'}}}
READY = {'status': 'completed', 'result': {'canExecute': True}}


class Clock:
    def __init__(self):
        self.milliseconds = 0
        self.sleeps = []

    def monotonic(self):
        return self.milliseconds / 1000

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.milliseconds += round(seconds * 1000)


class PreviewWorker:
    """Only preview admission and its operation receipt exist in this fake."""
    def __init__(self, clock, responses, operation_milliseconds=0):
        self.clock = clock
        self.responses = iter(responses)
        self.operation_milliseconds = operation_milliseconds
        self.calls = []
        self.receipts = []

    def call(self, method, params):
        if method != 'save.preview':
            raise AssertionError('Measurement must never replay save execution: ' + method)
        self.calls.append((method, copy.deepcopy(params), self.clock.milliseconds))
        return {'operationId': f'preview-{len(self.calls)}'}

    def operation(self, operation_id):
        self.receipts.append(operation_id)
        self.clock.milliseconds += self.operation_milliseconds
        return next(self.responses)


class PreviewAfterCancelTests(unittest.TestCase):
    def setUp(self):
        self.clock = Clock()
        self.params = {'jobId': 'synthetic', 'expectedReviewRevision': 7, 'async': True}
        self.time_patch = patch.object(measurement, 'time', self.clock)
        self.time_patch.start()
        self.addCleanup(self.time_patch.stop)

    def test_job_busy_then_success_records_retries_without_executing_save(self):
        worker = PreviewWorker(self.clock, [BUSY, BUSY, READY])
        result = measurement.preview_after_cancel(worker, self.params)
        self.assertEqual(result, {'busyResponses': 2, 'elapsedMs': 100.0})
        self.assertEqual(worker.calls, [('save.preview', self.params, value) for value in (0, 50, 100)])
        self.assertEqual(worker.receipts, ['preview-1', 'preview-2', 'preview-3'])
        self.assertEqual(self.clock.sleeps, [.05, .05])
        self.assertEqual(self.params['expectedReviewRevision'], 7)

    def test_other_errors_cancellation_and_blocked_preview_fail_without_retry(self):
        failures = [
            {'status': 'failed', 'error': {'data': {'reason': 'SOURCE_CHANGED'}}},
            {'status': 'failed', 'error': {'message': 'No structured error reason'}},
            {'status': 'canceled'},
            {'status': 'completed', 'result': {'canExecute': False}},
        ]
        for response in failures:
            with self.subTest(response=response):
                worker = PreviewWorker(self.clock, [response])
                with self.assertRaises(AssertionError) as raised:
                    measurement.preview_after_cancel(worker, self.params)
                self.assertIs(raised.exception.args[0], response)
                self.assertEqual(len(worker.calls), 1)
                self.assertEqual(worker.receipts, ['preview-1'])
                self.assertEqual(self.clock.sleeps, [])

    def test_synchronous_rpc_error_is_not_retried(self):
        error = AssertionError({'error': {'data': {'reason': 'JOB_BUSY'}}})
        worker = PreviewWorker(self.clock, [])
        with patch.object(worker, 'call', side_effect=error) as call:
            with self.assertRaises(AssertionError) as raised:
                measurement.preview_after_cancel(worker, self.params)
        self.assertIs(raised.exception, error)
        call.assert_called_once_with('save.preview', self.params)
        self.assertEqual(worker.receipts, [])
        self.assertEqual(self.clock.sleeps, [])

    def test_persistent_busy_stops_at_deadline_before_another_preview(self):
        worker = PreviewWorker(self.clock, [BUSY] * 1000)
        with self.assertRaisesRegex(TimeoutError, 'retry window.*10 seconds'):
            measurement.preview_after_cancel(worker, self.params)
        self.assertEqual(self.clock.milliseconds, 10_000)
        self.assertEqual(len(worker.calls), 200)
        self.assertEqual(worker.calls[-1][2], 9_950)
        self.assertTrue(all(start < 10_000 for _, _, start in worker.calls))
        self.assertEqual(len(worker.receipts), len(worker.calls))

    def test_final_sleep_is_limited_to_remaining_window(self):
        worker = PreviewWorker(self.clock, [BUSY, READY], operation_milliseconds=9_980)
        with self.assertRaises(TimeoutError):
            measurement.preview_after_cancel(worker, self.params)
        self.assertEqual(len(worker.calls), 1)
        self.assertEqual(len(self.clock.sleeps), 1)
        self.assertAlmostEqual(self.clock.sleeps[0], .02)
        self.assertEqual(self.clock.milliseconds, 10_000)

    def test_already_started_operation_keeps_its_normal_timeout(self):
        worker = PreviewWorker(self.clock, [READY], operation_milliseconds=12_000)
        result = measurement.preview_after_cancel(worker, self.params)
        self.assertEqual(result, {'busyResponses': 0, 'elapsedMs': 12_000.0})
        self.assertEqual(len(worker.calls), 1)
        self.assertEqual(self.clock.sleeps, [])


if __name__ == '__main__':
    unittest.main()
