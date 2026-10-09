#!/usr/bin/env python3
"""Exercise source → standalone image package → source on one temporary Compose deployment."""
import argparse
import json
import os
from pathlib import Path
import runpy
import sqlite3
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]
HELPERS = runpy.run_path(str(ROOT / "docker/smoke-test.py"))
docker, request = HELPERS["docker"], HELPERS["request"]


def run(arguments, env):
    subprocess.run(list(map(str, arguments)), cwd=ROOT, env=env, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="bakabase:local")
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()
    unique = uuid.uuid4().hex[:12]
    project = "bakabase-compose-" + unique
    packaged_image = "bakabase-compose-package:" + unique
    env = {key: value for key, value in os.environ.items()
           if not key.startswith(("BAKABASE_", "COMPOSE_"))}
    env.update(COMPOSE_PROJECT_NAME=project, BAKABASE_COMPOSE_OVERRIDE="")
    with tempfile.TemporaryDirectory(prefix="bakabase-compose-upgrade-") as temporary:
        root = Path(temporary)
        data, package = root / "appdata", root / "package"
        data.mkdir()
        env_file = root / ".env"
        env_file.write_text(f'BAKABASE_DATA_DIR="{data}"\nBAKABASE_PORT=0\nBAKABASE_BIND_ADDRESS=127.0.0.1\n')
        env["BAKABASE_ENV_FILE"] = str(env_file)
        # Apply a UID only to the fixture so Linux CI can inspect/remove its data.
        override = root / "fixture.yaml"
        override.write_text(f'services:\n  bakabase:\n    user: "{os.getuid()}:{os.getgid()}"\n    environment:\n      HOME: /tmp\n')
        env["BAKABASE_COMPOSE_OVERRIDE"] = str(override)
        base = [ROOT / "docker/image.sh"]
        device_id, name, previous_id = None, None, None
        marker = data / "compose-upgrade-marker.txt"
        try:
            docker("tag", args.image, packaged_image)
            run([ROOT / "docker/package.sh", package, packaged_image], env)
            docker("image", "rm", packaged_image)
            docker("load", "-i", str(package / "bakabase-image.tar"))
            for mode in ("source", "image", "source"):
                env["BAKABASE_IMAGE"] = packaged_image if mode == "image" else args.image
                if mode == "source":
                    run([ROOT / "docker/source.sh", "up", "-d", "--build", "--force-recreate"], env)
                else:
                    # Run solely from the exported package's Compose file, with
                    # no build definition or checkout dependency in this mode.
                    run(["docker", "compose", "--project-directory", package, "--env-file", env_file,
                         "-f", package / "compose.yaml", "-f", override,
                         "up", "-d", "--no-build", "--force-recreate"], env)
                container = subprocess.check_output([*map(str, base), "ps", "-q", "bakabase"], env=env, text=True).strip()
                inspect = json.loads(docker("inspect", container))[0]
                if previous_id == inspect["Id"]:
                    raise RuntimeError("Compose did not replace the previous container")
                previous_id = inspect["Id"]
                if name is None:
                    name = inspect["Name"]
                if inspect["Name"] != name or inspect["Config"]["Labels"]["com.docker.compose.project"] != project:
                    raise RuntimeError("Switching deployment modes changed the Compose identity")
                port = inspect["NetworkSettings"]["Ports"]["34567/tcp"][0]["HostPort"]
                address = "http://127.0.0.1:" + port
                if device_id is None:
                    HELPERS["initialize_empty"](container, address, data, args.timeout)
                    marker.write_text(unique)
                HELPERS["wait_for_app"](container, address, args.timeout)
                current = json.loads(request(address, "/app/analytics-info"))["data"]["deviceId"]
                if device_id is not None and current != device_id:
                    raise RuntimeError("Switching deployment modes lost the device identity")
                device_id = current
                if marker.read_text() != unique:
                    raise RuntimeError("Switching deployment modes replaced AppData")
                run([*base, "stop", "bakabase"], env)
                for database in data.glob("*.db"):
                    with sqlite3.connect(database) as connection:
                        if connection.execute("PRAGMA quick_check").fetchone() != ("ok",):
                            raise RuntimeError("SQLite integrity failed across a mode switch")
                print(f"PASS: {mode}; same Compose service/container name, persistent identity and SQLite", flush=True)
        finally:
            subprocess.run([*map(str, base), "down", "--remove-orphans"], env=env, check=False)
            docker("image", "rm", packaged_image, check=False)


if __name__ == "__main__":
    main()
