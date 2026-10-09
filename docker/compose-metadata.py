#!/usr/bin/env python3
"""Inject display-only mount metadata derived from the exact resolved Compose deployment."""
import json
import os
import signal
import subprocess
import sys

VARIABLE = "BAKABASE_DEPLOYMENT_MOUNTS"


def mount_metadata(config):
    service = config["services"]["server"]
    if service.get("volumes_from"):
        # Inherited mounts are not listed in this model and may shadow a known bind.
        return ""
    mounts = []
    for volume in service.get("volumes", []):
        kind = volume["type"]
        if kind not in ("bind", "volume", "tmpfs"):
            # Unknown mount kinds can obscure another bind. Do not emit an incomplete map.
            return ""
        # `compose config` emits a reloadable Compose document, including $$ escaping
        # in resolved paths. Undo that serialization once before storing plain metadata.
        entry = {"type": kind, "target": volume["target"].replace("$$", "$"), "readOnly": volume.get("read_only", False)}
        if kind == "bind":
            entry["source"] = volume["source"].replace("$$", "$")
        mounts.append(entry)
    for tmpfs in service.get("tmpfs", []):
        target, _, flags = tmpfs.replace("$$", "$").partition(":")
        mounts.append({"type": "tmpfs", "target": target, "readOnly": "ro" in flags.split(",")})
    result = json.dumps({"schemaVersion": 1, "readOnlyRoot": service.get("read_only", False), "mounts": mounts},
                        ensure_ascii=True, separators=(",", ":"))
    if len(result) > 65536 or len(mounts) > 256:
        return ""
    return result


def command_metadata(config, command):
    # `run -v` adds mounts outside the Compose model. Without interpreting a second mount
    # syntax, the truthful fallback is no host-path display for that one-off container.
    one_off_mounts = command[0] == "run" and any(
        arg == "--volume" or arg.startswith(("--volume=", "-v")) for arg in command[1:])
    return "" if one_off_mounts else mount_metadata(config)


def main():
    count = int(sys.argv[1])
    compose = sys.argv[2:2 + count]
    command = sys.argv[2 + count:]
    resolved = subprocess.run([*compose, "config", "--format", "json"], check=True, capture_output=True, text=True)
    config = json.loads(resolved.stdout)
    # Environment values substituted by Compose are not parsed as another Compose file:
    # literal dollars/backslashes in host paths survive without a second interpolation.
    environment = {**os.environ, VARIABLE: command_metadata(config, command)}
    child = subprocess.Popen([*compose, *command], env=environment)
    previous = signal.signal(signal.SIGTERM, lambda sig, frame: child.send_signal(sig))
    try:
        return child.wait()
    except KeyboardInterrupt:
        # The terminal sends SIGINT to both processes. Give Compose its ordinary shutdown.
        return child.wait()
    finally:
        signal.signal(signal.SIGTERM, previous)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except subprocess.CalledProcessError as error:
        if error.stderr:
            print(error.stderr, file=sys.stderr, end="")
        sys.exit(error.returncode)
    except (KeyError, ValueError, OSError) as error:
        print(f"Could not prepare Compose mount display information: {error}", file=sys.stderr)
        sys.exit(1)
