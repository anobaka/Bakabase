"""Exercise the test runner in separate, user-owned scratch folders."""
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest

WRAPPER = str(Path(__file__).resolve().parents[1] / "with-scratch.sh")
# A real TestKit host uses the same per-process directory convention.
SETUP = """
from pathlib import Path
import os, sys, time, uuid
run = Path(os.environ['BAKABASE_SCRATCH_RUN_DIR'])
root = Path(os.environ['BAKABASE_TEST_FIXTURE_ROOT'])
host = root / ('host-%d-%s' % (os.getpid(), uuid.uuid4().hex))
host.mkdir()
leaf = host / ('BakabaseTests_' + uuid.uuid4().hex)
leaf.mkdir()
db = leaf / 'test.db'
db.write_bytes(b'known')
identity = db.stat()
(host / (leaf.name + '.sqlite-owner')).write_text('test.db' + chr(10) + f'{identity.st_dev:X}:{identity.st_ino:X}')
(run / 't' / ('BakabaseTests_' + 'a' * 32)).mkdir()
(run / 't' / 'caller-owned').write_text('keep')
print(run, flush=True)
"""


@unittest.skipUnless(os.name == "posix", "with-scratch supports Unix test runs")
class TestScratchLifetime(unittest.TestCase):
    def setUp(self):
        run = os.environ.get("BAKABASE_SCRATCH_RUN_DIR")
        self.root = Path(run).parent if run else Path(os.environ.get("BAKABASE_SCRATCH_ROOT", tempfile.gettempdir())).resolve()
        if len(os.fsencode(str(self.root / "bb.XXXXXX" / "t") + "/")) > 50:
            self.skipTest("Use with-scratch and a short scratch root for Unix socket tests")

    def launch(self, program):
        return subprocess.Popen([WRAPPER, "--root", str(self.root), "--test-run", "--",
                                 sys.executable, "-c", SETUP + program],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def finish(self, child, expected):
        out, error = child.communicate(timeout=20)
        self.assertEqual(child.returncode, expected, (out, error))
        run = Path(out.splitlines()[0])
        self.assertEqual(run.parent, self.root)
        self.addCleanup(shutil.rmtree, run)
        return run

    def test_parallel_success_reclaims_registered_database_and_reserved_sidecars(self):
        for_success = """
for suffix in ('-wal', '-shm', '-journal'):
    (leaf / ('test.db' + suffix)).write_bytes(b'sidecar')
(run / 'evidence.log').write_text('evidence')
time.sleep(.05)
"""
        children = [self.launch(for_success) for _ in range(2)]
        dirs = [self.finish(child, 0) for child in children]
        self.assertNotEqual(dirs[0], dirs[1])
        for run in dirs:
            self.assertFalse(list((run / 'test-databases').iterdir()))
            self.assertEqual((run / 't' / 'caller-owned').read_text(), 'keep')
            self.assertEqual((run / 'evidence.log').read_text(), 'evidence')
            self.assertTrue((run / 't' / ('BakabaseTests_' + 'a' * 32)).is_dir())

    def test_unregistered_siblings_and_nested_explicit_databases_survive(self):
        run = self.finish(self.launch("""
(leaf / 'evidence.txt').write_text('sentinel')
(leaf / 'unregistered.db').write_text('caller data')
(leaf / 'explicit').mkdir()
(leaf / 'explicit' / 'test.db').write_text('nested database')
(leaf / 'external').symlink_to(run / 't', target_is_directory=True)
explicit = host / ('BakabaseTests_' + '1' * 32)
explicit.mkdir()
(explicit / 'test.db').write_text('explicit sibling')
"""), 0)
        root = run / 'test-databases'
        leaf = next(root.glob('host-*/BakabaseTests_*/evidence.txt')).parent
        self.assertFalse((leaf / 'test.db').exists())
        self.assertEqual((leaf / 'evidence.txt').read_text(), 'sentinel')
        self.assertEqual((leaf / 'unregistered.db').read_text(), 'caller data')
        self.assertEqual((leaf / 'explicit' / 'test.db').read_text(), 'nested database')
        self.assertTrue((leaf / 'external').is_symlink())
        self.assertEqual((leaf.parent / ('BakabaseTests_' + '1' * 32) / 'test.db').read_text(), 'explicit sibling')

    def test_replaced_database_preserves_old_and_new_files_and_sidecars(self):
        run = self.finish(self.launch("""
db.rename(leaf / 'original')
db.write_bytes(b'user replacement')
(leaf / 'test.db-wal').write_bytes(b'user WAL')
"""), 0)
        leaf = next((run / 'test-databases').glob('host-*/BakabaseTests_*/original')).parent
        self.assertEqual((leaf / 'test.db').read_bytes(), b'user replacement')
        self.assertEqual((leaf / 'test.db-wal').read_bytes(), b'user WAL')
        self.assertEqual((leaf / 'original').read_bytes(), b'known')

    def test_failure_and_command_signal_keep_evidence(self):
        for code, program in [(17, "sys.exit(17)"), (-signal.SIGTERM, "os.kill(os.getpid(),15)")]:
            run = self.finish(self.launch(program), code)
            self.assertEqual(len(list((run / 'test-databases').glob('host-*/BakabaseTests_*/test.db'))), 1)

    def test_signal_to_wrapper_reaches_child_even_when_child_handles_it(self):
        child = self.launch("""
import shutil
import signal
signal.signal(signal.SIGTERM, lambda sig, frame: sys.exit(0))
(run / 'ready').touch()
time.sleep(15)
""")
        run = Path(child.stdout.readline().strip())
        self.assertEqual(run.parent, self.root)
        self.addCleanup(shutil.rmtree, run)
        for _ in range(200):
            if (run / 'ready').exists(): break
            time.sleep(.02)
        self.assertTrue((run / 'ready').exists())
        child.terminate()
        _, error = child.communicate(timeout=5)
        self.assertEqual(child.returncode, 0, error)
        self.assertEqual(len(list((run / 'test-databases').glob('host-*/BakabaseTests_*/test.db'))), 1)

    def test_background_child_keeps_fixtures(self):
        child = self.launch("""
import subprocess
other = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(15)'],
                         stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
(run / 'background').write_text(str(other.pid))
""")
        run = self.finish(child, 0)
        try:
            self.assertTrue(list((run / 'test-databases').glob('host-*/BakabaseTests_*/test.db')))
        finally:
            os.kill(int((run / 'background').read_text()), signal.SIGTERM)

    def test_detached_reader_or_mmap_keeps_fixtures(self):
        for method in ["open", "mmap"]:
            with self.subTest(method=method):
                reader = """
import ctypes, os, sys, time
from pathlib import Path
f = open(sys.argv[1], 'rb')
if sys.argv[3] == 'mmap':
    libc = ctypes.CDLL(None, use_errno=True)
    libc.mmap.restype = ctypes.c_void_p
    address = libc.mmap(None, 5, 1, 2, f.fileno(), 0)
    assert address != ctypes.c_void_p(-1).value
    f.close()
    if sys.platform.startswith('linux'):
        assert not any(os.path.realpath(p) == sys.argv[1] for p in Path('/proc/self/fd').iterdir())
Path(sys.argv[2]).touch()
time.sleep(15)
"""
                child = self.launch("""
import subprocess
reader = subprocess.Popen([sys.executable, '-c', %r, str(db), str(run / 'opened'), %r],
                         start_new_session=True, close_fds=True, stdin=subprocess.DEVNULL,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
(run / 'reader').write_text(str(reader.pid))
for _ in range(200):
    if (run / 'opened').is_file(): break
    time.sleep(.02)
assert (run / 'opened').is_file()
""" % (reader, method))
                run = self.finish(child, 0)
                try:
                    self.assertTrue(list((run / 'test-databases').glob('host-*/BakabaseTests_*/test.db')))
                finally:
                    os.kill(int((run / 'reader').read_text()), signal.SIGTERM)

    def test_replaced_root_leaves_original_and_target_intact(self):
        child = self.launch("""
root.rename(run / 'moved')
root.symlink_to(run / 't', target_is_directory=True)
""")
        run = self.finish(child, 0)
        self.assertTrue(list((run / 'moved').glob('host-*/BakabaseTests_*/test.db')))
        self.assertEqual((run / 't' / 'caller-owned').read_text(), 'keep')


if __name__ == '__main__':
    unittest.main()
