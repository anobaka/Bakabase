"""Run one test command and retire its own implicit SQLite fixtures after success."""
import os
import re
import signal
import stat
import subprocess
import sys


def active(pid, group=False):
    try:
        (os.killpg if group else os.kill)(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def has_open_file(root):
    # A detached reader may have left both the command's process group and its
    # testhost. Inspect open handles without reading any database content.
    if sys.platform.startswith("linux"):
        for pid in os.listdir("/proc"):
            if not pid.isdecimal() or int(pid) == os.getpid():
                continue
            process = os.path.join("/proc", pid)
            try:
                if os.stat(process).st_uid != os.geteuid():
                    continue
                paths = [os.path.join(process, "cwd")]
                paths += [link.path for link in os.scandir(os.path.join(process, "fd"))]
                # mmap can outlive the last fd (SQLite commonly maps its SHM).
                with open(os.path.join(process, "maps"), "rb") as mappings:
                    if any(os.fsencode(root + os.sep) in line for line in mappings):
                        return True
                for link in paths:
                    try:
                        target = os.readlink(link)
                    except FileNotFoundError:
                        continue
                    if target == root or target.startswith(root + os.sep):
                        return True
            except (FileNotFoundError, ProcessLookupError):
                continue
        return False
    result = subprocess.run(["lsof", "-nP", "-t", "+D", root], capture_output=True,
                            text=True, timeout=30)
    if result.returncode not in (0, 1) or result.stderr.strip():
        raise RuntimeError("could not determine whether fixtures are in use")
    return any(pid != str(os.getpid()) for pid in result.stdout.splitlines())


def same_entry(parent, name, expected):
    return os.path.samestat(expected, os.stat(name, dir_fd=parent, follow_symlinks=False))


def retire_database(host, receipt):
    match = re.fullmatch(r"(BakabaseTests_(Template_)?[0-9a-f]{32})\.sqlite-owner", receipt)
    if not match:
        return
    leaf = match.group(1)
    primary = "template.db" if match.group(2) else "test.db"
    owner = os.open(receipt, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=host)
    try:
        receipt_info = os.fstat(owner)
        if not stat.S_ISREG(receipt_info.st_mode) or receipt_info.st_nlink != 1 or receipt_info.st_size > 100:
            return
        recorded = os.read(owner, 101)
    finally:
        os.close(owner)
    directory = os.open(leaf, os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=host)
    try:
        info = os.stat(primary, dir_fd=directory, follow_symlinks=False)
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or recorded != (
                f"{primary}\n{info.st_dev:X}:{info.st_ino:X}".encode("ascii")):
            return
        files = [primary + suffix for suffix in ("-wal", "-shm", "-journal", "")]
        names = os.listdir(directory)
        present = {name: os.stat(name, dir_fd=directory, follow_symlinks=False) for name in files if name in names}
        if primary not in present or not same_entry(host, receipt, receipt_info):
            return
        if any(not stat.S_ISREG(item.st_mode) or item.st_nlink != 1 for item in present.values()):
            return
        for name, item in present.items():
            if not same_entry(directory, primary, info) or not same_entry(directory, name, item):
                return
            os.unlink(name, dir_fd=directory)
        if same_entry(host, receipt, receipt_info):
            os.unlink(receipt, dir_fd=host)
        if not os.listdir(directory) and same_entry(host, leaf, os.fstat(directory)):
            os.rmdir(leaf, dir_fd=host)
    finally:
        os.close(directory)


def clean(root, fd, process_group):
    # No recursive deletion. The open root and each O_NOFOLLOW directory fd
    # keep all file operations bound to the identified directory.
    if not os.path.samestat(os.fstat(fd), os.stat(root, follow_symlinks=False)):
        raise RuntimeError("database directory was replaced")
    hosts = [(name, match) for name in os.listdir(fd)
             if (match := re.fullmatch(r"host-([1-9][0-9]*)-[0-9a-f]{32}", name))]
    if not hosts:
        return
    if active(process_group, group=True) or any(active(int(match.group(1))) for _, match in hosts):
        raise RuntimeError("a testhost or background process is still active")
    if has_open_file(root):
        raise RuntimeError("a process still has the database directory open")
    if not os.path.samestat(os.fstat(fd), os.stat(root, follow_symlinks=False)):
        raise RuntimeError("database directory changed while checking its users")
    for name, _ in hosts:
        host = os.open(name, os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
        try:
            for receipt in os.listdir(host):
                try:
                    retire_database(host, receipt)
                except OSError as error:
                    print(f"with-scratch: retained database entry {receipt}: {error}", file=sys.stderr)
            if not os.listdir(host) and same_entry(fd, name, os.fstat(host)):
                os.rmdir(name, dir_fd=fd)
        finally:
            os.close(host)


def run(args):
    root = os.path.join(os.environ["BAKABASE_SCRATCH_RUN_DIR"], "test-databases")
    os.mkdir(root, 0o700)
    fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    signals = (signal.SIGHUP, signal.SIGINT, signal.SIGQUIT, signal.SIGTERM)
    child = None
    interrupted = False
    pending = []

    def forward(signum, _frame):
        nonlocal interrupted
        interrupted = True
        if child is None:
            pending.append(signum)
        elif child.returncode is None:
            try:
                os.killpg(child.pid, signum)
            except ProcessLookupError:
                pass

    try:
        for sig in signals:
            if signal.getsignal(sig) != signal.SIG_IGN:
                signal.signal(sig, forward)
        try:
            child = subprocess.Popen(args, env={**os.environ, "BAKABASE_TEST_FIXTURE_ROOT": root},
                                     start_new_session=True)
        except FileNotFoundError as error:
            print("with-scratch:", error, file=sys.stderr)
            return 127
        except PermissionError as error:
            print("with-scratch:", error, file=sys.stderr)
            return 126
        for sig in pending:
            try:
                os.killpg(child.pid, sig)
            except ProcessLookupError:
                pass
        status = child.wait()
        if status == 0 and not interrupted:
            try:
                clean(root, fd, child.pid)
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                print(f"with-scratch: retained {root}: {error}", file=sys.stderr)
        else:
            print(f"with-scratch: retained test databases: {root}", file=sys.stderr)
    finally:
        os.close(fd)
    if status < 0:
        sig = -status
        if sig not in (signal.SIGKILL, signal.SIGSTOP):
            signal.signal(sig, signal.SIG_DFL)
        os.kill(os.getpid(), sig)
    return status


if __name__ == "__main__":
    sys.exit(run(sys.argv[1:]))
