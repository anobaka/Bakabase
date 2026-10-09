#!/usr/bin/env python3
"""Deployment metadata: real Compose parsing; all container-creating commands intercepted."""
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
HELPER = runpy.run_path(str(ROOT / "docker/compose-metadata.py"))
VARIABLE = HELPER["VARIABLE"]
ENDPOINT_VARIABLE = HELPER["ENDPOINT_VARIABLE"]


class ComposeMetadata(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="bakabase-mount-contract-")
        self.root = Path(self.temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.real_docker = shutil.which("docker") or "/usr/local/bin/docker"
        self.env = {key: value for key, value in os.environ.items()
                    if not key.startswith(("BAKABASE_", "COMPOSE_")) and key not in ("DOCKER_CONTEXT", "DOCKER_HOST")}
        self.env_file = self.root / ".env"
        self.env_file.write_text('BAKABASE_IMAGE=bakabase:contract\nBAKABASE_DATA_DIR="/host/initial"\n')
        self.env.update(BAKABASE_ENV_FILE=str(self.env_file), BAKABASE_COMPOSE_OVERRIDE="",
                        BAKABASE_VERSION="2.4.0-beta.511",
                        PATH=str(self.bin) + os.pathsep + self.env.get("PATH", ""))
        # Delegate only config to Docker. `up/create/run` print the environment and final
        # resolved service mounts instead of performing any daemon operation.
        fake = self.bin / "docker"
        fake.write_text(f'''#!{sys.executable}
import json, os, subprocess, sys
args = sys.argv[1:]
if args[:2] == ["context", "inspect"]:
    print(json.dumps("unix:///contract/docker.sock"))
    sys.exit(0)
commands = [i for i, arg in enumerate(args) if arg in ("up", "create", "run")]
if not commands:
    os.execv({self.real_docker!r}, [{self.real_docker!r}, *args])
index = commands[0]
cfg = json.loads(subprocess.check_output([{self.real_docker!r}, *args[:index], "config", "--format", "json"], text=True))
service = cfg["services"]["server"]
print(json.dumps({{"args": args, "metadata": os.environ.get({VARIABLE!r}), "serviceMetadata": service["environment"].get({VARIABLE!r}), "endpoints": os.environ.get({ENDPOINT_VARIABLE!r}), "serviceEndpoints": service["environment"].get({ENDPOINT_VARIABLE!r}), "mounts": service.get("volumes", []), "name": cfg["name"]}}))
''')
        fake.chmod(0o755)

    def tearDown(self):
        self.temporary.cleanup()

    def start(self, mode="image", *args):
        result = subprocess.run([str(ROOT / "docker/compose.sh"), mode, *(args or ("up", "-d"))],
                                env=self.env, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)

    def test_source_and_image_derive_same_mounts_without_second_interpolation(self):
        for name in ("literal $UNEXPANDED data", "${UNEXPANDED} and $$ data"):
            with self.subTest(name=name):
                self.env["BAKABASE_DATA_DIR"] = str(self.root / name)
                self.env["BAKABASE_DOWNLOADS_DIR"] = str(self.root / (name + " downloads"))
                image, source = self.start(), self.start("source")
                self.assertEqual(image["metadata"], source["metadata"])
                self.assertEqual(image["endpoints"], source["endpoints"])
                self.assertEqual(json.loads(image["endpoints"])["addresses"], [])
                # config output escapes dollars for reloading as Compose input; inspect
                # its plain value after undoing this one serialization, like mount_metadata.
                self.assertEqual(image["metadata"], image["serviceMetadata"].replace("$$", "$"))
                manifest = json.loads(image["metadata"])
                self.assertEqual(manifest["mounts"], [{"type": "bind", "target": "/data", "readOnly": False,
                                                      "source": self.env["BAKABASE_DATA_DIR"]},
                                                     {"type": "bind", "target": "/downloads", "readOnly": False,
                                                      "source": self.env["BAKABASE_DOWNLOADS_DIR"]}])

    def test_cli_env_and_mount_overrides_feed_both_config_and_start(self):
        env_file = self.root / "other.env"
        env_file.write_text('BAKABASE_DATA_DIR="/host/overridden"\n')
        override = self.root / "override.json"
        override.write_text(json.dumps({"services": {"server": {"read_only": True, "volumes": [
            {"type": "bind", "source": str(self.root / "readonly"), "target": "/data/logs", "read_only": True},
            {"type": "volume", "source": "cache", "target": "/data/cache"}]}}, "volumes": {"cache": {}}}))
        result = self.start("image", "--env-file", str(env_file), "-f", str(override), "-p", "mount-contract", "create")
        manifest = json.loads(result["metadata"])
        self.assertEqual(result["name"], "mount-contract")
        self.assertTrue(manifest["readOnlyRoot"])
        mounts = {mount["target"]: mount for mount in manifest["mounts"]}
        self.assertEqual(mounts["/data"]["source"], "/host/overridden")
        self.assertTrue(mounts["/data/logs"]["readOnly"])
        self.assertEqual(mounts["/data/cache"], {"type": "volume", "target": "/data/cache", "readOnly": False})

    def test_published_endpoint_uses_final_env_and_reaches_both_deployment_modes(self):
        env_file = self.root / "port.env"
        env_file.write_text("BAKABASE_PORT=45678\nBAKABASE_BIND_ADDRESS=192.168.3.23\n")
        for mode in ("image", "source"):
            result = self.start(mode, "--env-file", str(env_file), "create")
            self.assertEqual(json.loads(result["endpoints"]),
                             {"schemaVersion": 1, "addresses": ["http://192.168.3.23:45678"]})
            self.assertEqual(result["endpoints"], result["serviceEndpoints"])

    def test_run_mount_overrides_fall_back_instead_of_publishing_stale_paths(self):
        result = self.start("image", "run", "--rm", "--volume", "/other:/data", "server")
        self.assertEqual(result["metadata"], "")
        self.assertEqual(result["serviceMetadata"], "")

    def test_standalone_package_entry_uses_its_own_compose_files(self):
        package = self.root / "standalone package"
        package.mkdir()
        for name in ("compose.yaml", "compose.sh", "image.sh", "compose-metadata.py"):
            shutil.copy2(ROOT / "docker" / name, package / name)
        result = subprocess.run([str(package / "image.sh"), "up", "-d", "--no-build"],
                                env=self.env, text=True, capture_output=True, check=True)
        value = json.loads(result.stdout)
        self.assertIn(str((package / "compose.yaml").resolve()), value["args"])
        self.assertNotIn(str(ROOT / "docker/compose.yaml"), value["args"])
        self.assertEqual(json.loads(value["metadata"])["mounts"][0]["source"], "/host/initial")

    def test_tmpfs_masks_bind_and_no_other_configuration_is_copied(self):
        raw = HELPER["mount_metadata"]({"services": {"server": {"environment": {"SECRET": "not-copied"},
            "volumes": [{"type": "bind", "source": "/host", "target": "/data"}], "tmpfs": ["/data/temp:ro,size=1m"]}}})
        self.assertNotIn("not-copied", raw)
        self.assertEqual(json.loads(raw)["mounts"][1], {"type": "tmpfs", "target": "/data/temp", "readOnly": True})

    def test_inherited_mounts_are_unknown_instead_of_incomplete(self):
        self.assertEqual(HELPER["mount_metadata"]({"services": {"server": {
            "volumes_from": ["other"], "volumes": [{"type": "bind", "source": "/host", "target": "/data"}]}}}), "")

    def test_non_start_commands_do_not_require_python(self):
        # This fake Docker is a shell script, and PATH deliberately contains no Python.
        (self.bin / "docker").write_text('#!/bin/bash\nprintf "docker-called\\n"\n')
        (self.bin / "bash").symlink_to("/bin/bash")
        dirname = shutil.which("dirname") or "/usr/bin/dirname"
        (self.bin / "dirname").symlink_to(dirname)
        env = {**self.env, "PATH": str(self.bin)}
        for command in ("config", "build", "pull", "down", "ps", "logs"):
            result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", command], env=env,
                                    text=True, capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(result.stdout, "docker-called\n")
        result = subprocess.run([str(ROOT / "docker/compose.sh"), "image", "up"], env=env, text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Python 3", result.stderr)


class PublishedEndpoints(unittest.TestCase):
    def config(self, ports, listening="34567"):
        return {"services": {"server": {"ports": ports, "environment": {"API_LISTENING_PORTS": listening}}}}

    def addresses(self, config, command=None, local=True, hosts=None):
        return json.loads(HELPER["endpoint_metadata"](config, command or ["up", "-d"], local,
                          hosts if hosts is not None else ["192.168.3.23"]))["addresses"]

    def test_only_actual_tcp_listener_mappings_produce_valid_endpoints(self):
        ports = [
            {"target": 34567, "published": "45678", "host_ip": "0.0.0.0"},
            {"target": 34567, "published": "45678", "host_ip": "192.168.3.23"},
            {"target": 4567, "published": "9000"},
            {"target": 34567, "published": "9001", "protocol": "udp"},
            {"target": 34567, "published": "9002", "host_ip": "127.0.0.1"},
            {"target": 34567, "published": "9003", "host_ip": "::"},
            {"target": 34567, "published": "0"},
            {"target": 34567, "published": "4000-5000"},
            {"target": 34567},
        ]
        self.assertEqual(self.addresses(self.config(ports), hosts=["192.168.3.23", "127.0.0.1", "0.0.0.0", "169.254.1.2"]),
                         ["http://192.168.3.23:45678"])

    def test_explicit_bind_and_changed_listener_ports_follow_resolved_config(self):
        config = self.config([{"target": 8080, "published": "80", "host_ip": "192.168.3.24"},
                              {"target": 9090, "published": "90"},
                              {"target": 34567, "published": "34567"}], "8080; 9090")
        self.assertEqual(self.addresses(config), ["http://192.168.3.24:80", "http://192.168.3.23:90"])
        config["services"]["server"]["environment"]["API_LISTENING_PORTS"] = "8080,bad"
        self.assertEqual(self.addresses(config), [])

    def test_unknown_host_remote_context_and_host_network_never_guess(self):
        config = self.config([{"target": 34567, "published": "34567"}])
        self.assertEqual(self.addresses(config, hosts=[]), [])
        self.assertEqual(self.addresses(config, local=False), [])
        config["services"]["server"]["network_mode"] = "host"
        self.assertEqual(self.addresses(config), [])

    def test_run_only_uses_unmodified_explicit_service_ports(self):
        config = self.config([{"target": 34567, "published": "45678"}])
        self.assertEqual(self.addresses(config, ["run", "--service-ports", "server"]), ["http://192.168.3.23:45678"])
        for args in (["run", "server"], ["run", "-p", "9000:34567", "server"],
                     ["run", "--service-ports", "--publish=9000:34567", "server"],
                     ["run", "--service-ports", "-eAPI_LISTENING_PORTS=9000", "server"],
                     ["run", "--service-ports", "--env-file", "other.env", "server"],
                     ["run", "--service-ports", "--entrypoint", "other", "server"]):
            with self.subTest(args=args):
                self.assertEqual(self.addresses(config, args), [])

    def test_context_precedence_and_failure_do_not_advertise_local_machine_for_remote_engine(self):
        function = HELPER["local_docker_context"]
        with patch.dict(function.__globals__, read_command=lambda args: json.dumps("ssh://nas")):
            self.assertFalse(function("docker", {"DOCKER_CONTEXT": "nas", "DOCKER_HOST": "unix:///local.sock"}))
            self.assertTrue(function("docker", {"DOCKER_HOST": "unix:///local.sock"}))
            for endpoint in ("tcp://127.0.0.1:2375", "tcp://192.168.3.24:2375", "npipe:////./pipe/docker_engine", "unix://nas/path"):
                self.assertFalse(function("docker", {"DOCKER_HOST": endpoint}))
        with patch.dict(function.__globals__, read_command=lambda args: json.dumps("unix:///local.sock")):
            self.assertTrue(function("docker", {}))
        with patch.dict(function.__globals__, read_command=lambda args: "invalid context output"):
            self.assertFalse(function("docker", {}))

    def test_mac_detects_lan_even_when_vpn_owns_default_route(self):
        function = HELPER["host_ipv4_addresses"]
        fixture = """lo0: flags=1<UP,RUNNING>
    inet 127.0.0.1 netmask 0xff000000
    status: active
en0: flags=1<UP,RUNNING>
    inet 192.168.3.23 netmask 0xffffff00
    status: active
en1: flags=1<UP,RUNNING>
    inet 192.168.4.23 netmask 0xffffff00
    status: inactive
utun5: flags=1<UP,RUNNING>
    inet 10.0.0.2 netmask 0xffffff00
    status: active
bridge0: flags=1<UP,RUNNING>
    inet 192.168.164.1 netmask 0xffffff00
    status: active
"""
        with patch.object(HELPER["platform"], "system", return_value="Darwin"), \
                patch.dict(function.__globals__, read_command=lambda args: fixture):
            self.assertEqual(function(), ["192.168.3.23"])

    def test_linux_filters_container_and_tunnel_interfaces_and_nested_container(self):
        function = HELPER["host_ipv4_addresses"]
        interfaces = [{"ifname": name, "link_type": "ether", "operstate": "UP", "linkinfo": {"info_kind": kind},
                       "addr_info": [{"family": "inet", "scope": "global", "local": address}]}
                      for name, kind, address in (("enp1s0", None, "192.168.3.23"),
                                                 ("docker0", "bridge", "172.17.0.1"),
                                                 ("veth1", "veth", "172.17.0.2"),
                                                 ("wg0", "wireguard", "10.0.0.1"),
                                                 ("tap0", "tun", "10.0.0.2"))]
        with patch.object(HELPER["platform"], "system", return_value="Linux"), \
                patch.object(Path, "exists", return_value=False), \
                patch.dict(function.__globals__, read_command=lambda args: json.dumps(interfaces)):
            self.assertEqual(function(), ["192.168.3.23"])
        with patch.object(HELPER["platform"], "system", return_value="Linux"), \
                patch.object(Path, "exists", return_value=True):
            self.assertEqual(function(), [])

    def test_missing_host_network_tools_is_an_empty_optional_hint(self):
        function = HELPER["host_ipv4_addresses"]
        def missing(args):
            raise FileNotFoundError("ip")
        with patch.object(HELPER["platform"], "system", return_value="Linux"), \
                patch.object(Path, "exists", return_value=False), \
                patch.dict(function.__globals__, read_command=missing):
            self.assertEqual(function(), [])


if __name__ == "__main__":
    unittest.main()
