#!/usr/bin/env python3
"""Build and run the headless server with replaceable programs and external AppData."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import uuid


REPO = Path(__file__).resolve().parents[1]
EXECUTABLE = "bakabase-server"


def absolute(path):
    if not str(path).strip():
        raise ValueError("Directory paths must not be empty.")
    return Path(path).expanduser().resolve()


def run_command(command, cwd=REPO, **kwargs):
    print(f"+ {shlex.join(map(str, command))}", flush=True)
    subprocess.run(list(map(str, command)), cwd=cwd, check=True, **kwargs)


def native_runtime():
    system = {"Darwin": "osx", "Linux": "linux"}.get(platform.system())
    machine = {"arm64": "arm64", "aarch64": "arm64", "x86_64": "x64", "AMD64": "x64"}.get(platform.machine())
    if not system or not machine:
        raise ValueError("This launcher supports macOS/Linux on arm64/x64.")
    return f"{system}-{machine}"


def release_path(install, name):
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", name):
        raise ValueError("Release must be a directory name returned by the list command.")
    release = install / "releases" / name
    if release.is_symlink() or release.resolve().parent != (install / "releases").resolve():
        raise ValueError("A release must be an ordinary directory inside releases/.")
    if not (release / EXECUTABLE).is_file() or not (release / "web/index.html").is_file():
        raise ValueError(f"Incomplete server release: {release}")
    return release


def activate(install, name):
    release_path(install, name)
    current = install / "current"
    if current.exists() and not current.is_symlink():
        raise ValueError(f"Refusing to replace an ordinary file/directory: {current}")
    link = install / f".current-{uuid.uuid4().hex}"
    try:
        link.symlink_to(Path("releases") / name, target_is_directory=True)
        os.replace(link, current)
    finally:
        link.unlink(missing_ok=True)
    print(f"Selected {name}. Restart the server to use it; AppData was not changed.")


def build(args, install):
    runtime = args.runtime or native_runtime()
    for dependency in ("src/libs/Bakabase.Infrastructures/Bakabase.Infrastructures/Bakabase.Infrastructures.csproj",
                       "src/libs/LazyMortal/src/Bootstrap/Bootstrap.csproj"):
        if not (REPO / dependency).is_file():
            raise ValueError("Submodules are missing. Run: git submodule update --init --recursive")
    if not shutil.which("dotnet"):
        raise ValueError(".NET 9 SDK was not found on PATH. See global.json.")
    web = absolute(args.web_dir) if args.web_dir else REPO / "src/web/dist"
    if args.web_dir:
        if not (web / "index.html").is_file():
            raise ValueError(f"Built frontend not found: {web}/index.html")
    else:
        yarn = ["corepack", "yarn"] if shutil.which("corepack") else ["yarn"]
        if not shutil.which(yarn[0]):
            raise ValueError("Install Node.js and Corepack (or Yarn 4.9.2) first.")
        run_command(yarn + ["install", "--immutable"], cwd=REPO / "src/web")
        run_command(yarn + ["postinstall"], cwd=REPO / "src/web")
        run_command(yarn + ["build"], cwd=REPO / "src/web")

    releases = install / "releases"
    releases.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".building-", dir=releases))
    commit = subprocess.check_output(["git", "rev-parse", "--short=12", "HEAD"], cwd=REPO, text=True).strip()
    name = f"{datetime.now(timezone.utc):%Y%m%dT%H%M%SZ}-{commit}-{uuid.uuid4().hex[:6]}"
    try:
        # DOCKER is the existing headless build mode; --runtime chooses the actual OS/CPU.
        run_command(["dotnet", "publish", "src/apps/Bakabase.Service/Bakabase.Service.csproj",
                     "-c", "Release", "-p:RuntimeMode=DOCKER", "--self-contained", "true",
                     "-r", runtime, "-o", staging])
        # macOS treats the .service suffix as a service bundle and may kill a bare
        # executable with that suffix. The apphost embeds its DLL name, so renaming
        # only the launcher keeps assembly identity and dependency loading intact.
        (staging / "Bakabase.Service").rename(staging / EXECUTABLE)
        shutil.copytree(web, staging / "web", dirs_exist_ok=True)
        with (staging / "release-check.json").open("w") as report:
            run_command([sys.executable, REPO / "src/scripts/check-release-contract.py",
                         "--role", "server", "--publish-dir", staging, "--require-web"], stdout=report)
        dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True))
        (staging / "release.json").write_text(json.dumps({
            "commit": commit, "dirty": dirty, "runtime": runtime,
            "builtAt": datetime.now(timezone.utc).isoformat(), "source": str(REPO),
        }, indent=2) + "\n")
        staging.rename(releases / name)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    print(f"Built {name}")
    if not args.no_activate:
        activate(install, name)


def run(args, install):
    current = install / "current"
    if not current.is_symlink():
        raise ValueError("No active release. Run the build command first.")
    # Resolve once: replacing current must never change a running process's content root.
    release = release_path(install, current.resolve().name)
    if current.resolve() != release.resolve():
        raise ValueError("current must point to a release inside this installation.")
    runtime = json.loads((release / "release.json").read_text()).get("runtime")
    if runtime != native_runtime():
        raise ValueError(f"This release targets {runtime}; this machine is {native_runtime()}.")
    data = absolute(args.data_dir) if args.data_dir else default_data_path()
    if data == install or install in data.parents or data in install.parents:
        raise ValueError("AppData and the program installation must be separate, non-nested directories.")
    ports = re.split(r"[,;]", args.ports)
    if not ports or any(not p.strip().isdigit() or not 1 <= int(p) <= 65535 for p in ports):
        raise ValueError("--ports must contain port numbers from 1 to 65535, separated by commas.")
    env = os.environ.copy()
    env.update(API_LISTENING_PORTS=args.ports, BAKABASE_BIND_ADDRESS=args.bind)
    if args.data_dir:
        env["BAKABASE_DATA_DIR"] = str(data)
    else:
        env.pop("BAKABASE_DATA_DIR", None)
    choice = "fixed" if args.data_dir else "choose during first-run setup"
    print(f"Release: {release.name}\nAppData anchor: {data} ({choice})\nListen: {args.bind}:{args.ports}", flush=True)
    extra = args.server_args[1:] if args.server_args[:1] == ["--"] else args.server_args
    os.chdir(release)
    os.execve(release / EXECUTABLE, [str(release / EXECUTABLE), *extra], env)


def default_data_path():
    home = Path.home()
    return (home / "Library/Application Support/Bakabase.Server" if platform.system() == "Darwin"
            else Path(os.environ.get("XDG_DATA_HOME", home / ".local/share")) / "Bakabase.Server")


def main():
    home = Path.home()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install-dir", default=os.environ.get("BAKABASE_SERVER_HOME", home / ".local/share/bakabase-server"),
                        help="Program releases and current symlink; also BAKABASE_SERVER_HOME")
    commands = parser.add_subparsers(dest="command", required=True)
    build_parser = commands.add_parser("build", help="Publish source, verify it, and select the new release")
    build_parser.add_argument("--runtime", choices=["osx-arm64", "osx-x64", "linux-arm64", "linux-x64"])
    build_parser.add_argument("--web-dir", help="Reuse a built frontend (for backend-only edits)")
    build_parser.add_argument("--no-activate", action="store_true", help="Build without switching current")
    run_parser = commands.add_parser("run", help="Run selected release in foreground; Ctrl-C stops it")
    run_parser.add_argument("--data-dir", default=os.environ.get("BAKABASE_DATA_DIR") or None,
                            help="Fix AppData to this directory; without it, first-run setup can choose the location")
    run_parser.add_argument("--ports", default=os.environ.get("API_LISTENING_PORTS", "34567"))
    run_parser.add_argument("--bind", default=os.environ.get("BAKABASE_BIND_ADDRESS", "127.0.0.1"))
    run_parser.add_argument("server_args", nargs=argparse.REMAINDER)
    commands.add_parser("activate", help="Select an existing release; does not modify or downgrade AppData").add_argument("release")
    commands.add_parser("list", help="List retained releases")
    args = parser.parse_args()
    try:
        install = absolute(args.install_dir)
        if args.command == "build":
            build(args, install)
        elif args.command == "activate":
            activate(install, args.release)
        elif args.command == "run":
            run(args, install)
        else:
            current = (install / "current").resolve()
            for release in sorted((install / "releases").glob("*")):
                if release.is_dir() and not release.name.startswith("."):
                    print(f"{'*' if release.resolve() == current else ' '} {release.name}")
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
