#!/usr/bin/env python3
"""Small runner boundary checks; no Docker daemon or product build required."""
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import signal
import subprocess
import sys
import types
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('cross_runner', Path(__file__).with_name('run-linux-cross-container.py'))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class CrossContainerRunnerTests(unittest.TestCase):
    def test_disk_floor_checks_build_volume_even_when_results_volume_has_space(self):
        def usage(path):
            return types.SimpleNamespace(free=1000 if str(path) == '/results' else 1)
        with patch.object(runner.time, 'monotonic', return_value=1), patch.object(runner.shutil, 'disk_usage', side_effect=usage):
            with self.assertRaisesRegex(RuntimeError, '/build-temp'):
                runner.check_budget(0, 30, 100, [Path('/results'), Path('/build-temp')])

    def test_expired_deadline_rejects_work_before_another_process_starts(self):
        with patch.object(runner.time, 'monotonic', return_value=31):
            with self.assertRaisesRegex(RuntimeError, 'deadline'):
                runner.check_budget(0, 30, 1, [])

    def test_cleanup_timeout_is_reported_without_masking_original_error(self):
        failure = subprocess.TimeoutExpired(['docker', 'rm'], 30)
        with patch.object(runner.subprocess, 'run', side_effect=failure):
            self.assertIn('timed out', runner.remove_owned_containers(['owned-container'])[0])

    def test_empty_check_selection_is_rejected_before_docker(self):
        with patch.object(sys, 'argv', ['runner', '--skip-smoke']), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                runner.main()
            self.assertEqual(error.exception.code, 2)

    @unittest.skipUnless(os.name == 'posix', 'runner supports POSIX build hosts')
    def test_descendant_ignoring_sigterm_is_killed_after_leader_exits(self):
        child_code = 'import signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); print("ready",flush=True); time.sleep(600)'
        parent_code = ('import subprocess,sys,time; '
                       f'child=subprocess.Popen([sys.executable,"-c",{child_code!r}],stdout=subprocess.PIPE,text=True); '
                       'print(child.pid,flush=True); print(child.stdout.readline().strip(),flush=True); time.sleep(600)')
        parent = subprocess.Popen([sys.executable, '-c', parent_code], stdout=subprocess.PIPE,
                                  stderr=subprocess.PIPE, text=True, start_new_session=True)
        try:
            child_pid = int(parent.stdout.readline().strip())
            self.assertEqual(parent.stdout.readline().strip(), 'ready')
            runner.stop_process_group(parent)
            self.assertIsNotNone(parent.poll())
            status = subprocess.run(['ps', '-o', 'stat=', '-p', str(child_pid)], capture_output=True, text=True)
            self.assertTrue(not status.stdout.strip() or status.stdout.strip().startswith('Z'), status.stdout)
        finally:
            try:
                os.killpg(parent.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            parent.wait(timeout=5)
            parent.stdout.close()
            parent.stderr.close()


if __name__ == '__main__':
    unittest.main()
