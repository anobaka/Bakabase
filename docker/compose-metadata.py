#!/usr/bin/env python3
"""Derive host paths and published endpoints from the exact Compose deployment."""
import ipaddress
import json
import os
from pathlib import Path
import platform
import re
import signal
import subprocess
import sys
from urllib.parse import urlparse

VARIABLE = "BAKABASE_DEPLOYMENT_MOUNTS"
ENDPOINT_VARIABLE = "BAKABASE_DEPLOYMENT_ENDPOINTS"


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


def read_command(command):
    return subprocess.check_output(command, text=True, stderr=subprocess.DEVNULL, timeout=3)


def local_docker_context(docker, environment):
    """A local socket is evidence of a local engine; TCP may be an SSH tunnel."""
    try:
        context = environment.get("DOCKER_CONTEXT")
        if context:
            endpoint = json.loads(read_command([docker, "context", "inspect", context,
                                               "--format", "{{json .Endpoints.docker.Host}}"]))
        elif environment.get("DOCKER_HOST"):
            endpoint = environment["DOCKER_HOST"]
        else:
            endpoint = json.loads(read_command([docker, "context", "inspect",
                                               "--format", "{{json .Endpoints.docker.Host}}"]))
        parsed = urlparse(endpoint)
        return parsed.scheme == "unix" and not parsed.netloc and parsed.path.startswith("/")
    except (OSError, ValueError, TypeError, subprocess.SubprocessError):
        return False


def usable_ipv4(value):
    try:
        address = ipaddress.ip_address(value)
        return (address.version == 4 and not (address.is_loopback or address.is_unspecified or
                address.is_link_local or address.is_multicast or address.is_reserved))
    except (ValueError, TypeError):
        return False


def host_ipv4_addresses():
    """Read active host interfaces, without a network probe or a Docker-network query."""
    try:
        system = platform.system()
        if system == "Darwin":
            # A VPN can own the default route. Ethernet/Wi-Fi still expose the LAN,
            # so inspect active physical interfaces instead of following that route.
            addresses = []
            for block in re.split(r"(?=^\S+: flags=)", read_command(["/sbin/ifconfig", "-a"]), flags=re.M):
                match = re.match(r"((?:en|bond)\d+): flags=\d+<([^>]+)>", block)
                if not match or "UP" not in match[2].split(",") or "status: active" not in block:
                    continue
                addresses.extend(re.findall(r"^\s+inet ([\d.]+)\s", block, flags=re.M))
        elif system == "Linux":
            # A wrapper run inside a container can reach a mounted host socket, but
            # its own eth0 is still a container interface, never the Docker host LAN.
            if Path("/.dockerenv").exists() or Path("/run/.containerenv").exists():
                return []
            interfaces = json.loads(read_command(["ip", "-details", "-json", "-4", "address", "show", "up"]))
            addresses = []
            for interface in interfaces:
                name = interface.get("ifname", "")
                if (not name or re.match(r"^(lo\b|docker|br|virbr|veth|tun|tap|utun|tailscale|wg|vbox|vmnet|cni|flannel|kube|podman)", name)
                        or interface.get("link_type") != "ether" or interface.get("operstate") != "UP"
                        or interface.get("linkinfo", {}).get("info_kind") not in (None, "bond", "vlan")):
                    continue
                addresses.extend(a.get("local", "") for a in interface.get("addr_info", [])
                                 if a.get("family") == "inet" and a.get("scope") == "global")
        else:
            return []
        return list(dict.fromkeys(address for address in addresses if usable_ipv4(address)))
    except (OSError, ValueError, TypeError, subprocess.SubprocessError):
        return []


def endpoint_metadata(config, command, local_context, host_addresses):
    service = config["services"]["server"]
    addresses = []
    # `run` does not publish service ports unless explicitly requested. CLI overrides
    # are outside the resolved model; avoid advertising a stale port or listener.
    run_safe = command[0] != "run" or ("--service-ports" in command and not any(
        a in ("-e", "--env", "--env-file", "--entrypoint", "-p", "--publish") or
        a.startswith(("--env=", "--env-file=", "--entrypoint=", "--publish=", "-p", "-e"))
        for a in command[1:]))
    if local_context and run_safe and service.get("network_mode") in (None, "bridge"):
        value = service.get("environment", {}).get("API_LISTENING_PORTS") or "34567"
        parts = re.split(r"[,;]", str(value))
        if all(p.strip().isdigit() and 1 <= int(p) <= 65535 for p in parts):
            targets = {int(p) for p in parts}
            for port in service.get("ports", []):
                published = str(port.get("published", ""))
                if (port.get("protocol", "tcp") != "tcp" or port.get("target") not in targets or
                        not published.isdigit() or not 1 <= int(published) <= 65535):
                    continue
                bind = port.get("host_ip") or "0.0.0.0"
                hosts = host_addresses if bind == "0.0.0.0" else [bind]
                for host in hosts:
                    if usable_ipv4(host):
                        addresses.append(f"http://{host}:{int(published)}")
    addresses = list(dict.fromkeys(addresses))
    return json.dumps({"schemaVersion": 1, "addresses": addresses if len(addresses) <= 32 else []},
                      separators=(",", ":"))


def main():
    count = int(sys.argv[1])
    compose = sys.argv[2:2 + count]
    command = sys.argv[2 + count:]
    resolved = subprocess.run([*compose, "config", "--format", "json"], check=True, capture_output=True, text=True)
    config = json.loads(resolved.stdout)
    # Environment values substituted by Compose are not parsed as another Compose file:
    # literal dollars/backslashes in host paths survive without a second interpolation.
    local_context = local_docker_context(compose[0], os.environ)
    endpoints = endpoint_metadata(config, command, local_context, host_ipv4_addresses() if local_context else [])
    environment = {**os.environ, VARIABLE: command_metadata(config, command), ENDPOINT_VARIABLE: endpoints}
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
        print(f"Could not prepare Compose deployment information: {error}", file=sys.stderr)
        sys.exit(1)
