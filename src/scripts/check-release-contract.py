#!/usr/bin/env python3
"""Read-only product identity and actual publish-content release gates (no feed access)."""
import argparse
import json
from pathlib import Path
import plistlib
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PRODUCTS = {
    "unified": {"project": "Bakabase.App", "assembly": "Bakabase", "bundle": "com.anobaka.bakabase",
                "host": "src/apps/Bakabase.Service/Components/BakabaseHost.cs",
                "source": "src/apps/Bakabase.Service/Components/BakabaseUpdateSource.cs",
                "dataEnv": "BAKABASE_DATA_DIR", "updateEnv": "BAKABASE_UPDATE_URL",
                "dataFolder": "Bakabase", "windowsDataFolder": "Bakabase.AppData",
                "feed": "https://cdn-public.anobaka.com/app/bakabase/releases/",
                "buildJob": "build-app", "deployJob": "deploy-velopack", "artifact": "vpk-"},
    "client": {"project": "Bakabase.Client.App", "assembly": "Bakabase.Client", "bundle": "com.anobaka.bakabase.client",
               "host": "src/apps/Bakabase.Client.Remoting/Components/ClientHost.cs",
               "source": "src/apps/Bakabase.Client.Remoting/Components/Updating/ClientUpdateSource.cs",
               "dataEnv": "BAKABASE_CLIENT_DATA_DIR", "updateEnv": "BAKABASE_CLIENT_UPDATE_URL",
               "dataFolder": "Bakabase.Client", "windowsDataFolder": "Bakabase.Client.AppData",
               "feed": "https://cdn-public.anobaka.com/app/bakabase-client/releases/",
               "buildJob": "build-client", "deployJob": "deploy-client-velopack", "artifact": "velopack-client-"},
}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def workflow_job(text, name):
    match = re.search(r"^  " + re.escape(name) + r":\s*\n(.*?)(?=^  [\w-]+:\s*\n|\Z)", text, re.M | re.S)
    require(match is not None, f"Missing workflow job {name}")
    return match.group(1)


def project_graph(project):
    seen, packages, pending = set(), set(), [project]
    while pending:
        path = pending.pop().resolve()
        if path in seen:
            continue
        seen.add(path)
        tree = ET.parse(path)
        packages.update(node.attrib["Include"] for node in tree.iter("PackageReference") if "Include" in node.attrib)
        pending.extend(path.parent / node.attrib["Include"].replace("\\", "/")
                       for node in tree.iter("ProjectReference"))
    return {path.stem for path in seen}, packages


def check_sources(root):
    build = (root / ".github/workflows/_build.yml").read_text(encoding="utf-8")
    deploy = (root / ".github/workflows/_deploy.yml").read_text(encoding="utf-8")
    release = (root / ".github/workflows/_release.yml").read_text(encoding="utf-8")
    profile = (root / "src/libs/Bakabase.Infrastructures/Bakabase.Infrastructures/Components/App/AppDataPathProfile.cs").read_text(encoding="utf-8")
    for role, product in PRODUCTS.items():
        directory = root / "src/apps" / product["project"]
        tree = ET.parse(directory / (product["project"] + ".csproj"))
        require(tree.findtext(".//AssemblyName") == product["assembly"], f"{role}: executable identity changed")
        with (directory / "Info.plist").open("rb") as stream:
            info = plistlib.load(stream)
            require(info["CFBundleIdentifier"] == product["bundle"], f"{role}: macOS bundle identity changed")
            require(info.get("CFBundleExecutable") == product["assembly"], f"{role}: macOS executable missing or incorrect")
        host = (root / product["host"]).read_text(encoding="utf-8")
        require(re.search(r'SingleInstanceId\s*=>\s*"' + re.escape(product["assembly"]) + r'"', host),
                f"{role}: single-instance identity changed")
        source = (root / product["source"]).read_text(encoding="utf-8")
        for key in ("updateEnv", "feed"):
            require('"' + product[key] + '"' in source, f"{role}: {key} changed")
        profile_name = "AllInOne" if role == "unified" else "Client"
        expected = r'\b' + profile_name + r'\s*=\s*new\(\s*"' + product["dataEnv"] + r'",\s*"' + re.escape(product["dataFolder"]) + r'",\s*"' + re.escape(product["windowsDataFolder"]) + r'"\s*\)'
        require(re.search(expected, profile), f"{role}: AppData profile changed")
        build_job = workflow_job(build, product["buildJob"])
        require(re.search(r"--packId\s+" + re.escape(product["assembly"]) + r"\s", build_job), f"{role}: pack ID changed")
        for executable in (product["assembly"], product["assembly"] + ".exe"):
            require(f'MAIN_EXE="{executable}"' in build_job, f"{role}: installer main executable changed")
        require(f'name: {product["artifact"]}${{{{ matrix.artifact }}}}' in build_job, f"{role}: build artifact identity changed")
        deploy_job = workflow_job(deploy, product["deployJob"])
        expected_prefix = product["feed"].replace("https://cdn-public.anobaka.com/", "oss://anobaka-public/")
        require(expected_prefix in deploy_job, f"{role}: published updater prefix changed")
        other = PRODUCTS["client" if role == "unified" else "unified"]
        forbidden = other["feed"].replace("https://cdn-public.anobaka.com/", "oss://anobaka-public/")
        require(forbidden not in deploy_job, f"{role}: another product's updater feed appears in the deployment job")
        require(f'pattern: {product["artifact"]}*' in release, f"{role}: release artifact collection missing")
    require("AppDataAnchor.Use(AppDataPathProfile.Client)" in (root / "src/apps/Bakabase.Client.App/Program.cs").read_text(encoding="utf-8"),
            "The legacy client no longer selects its own AppData profile")
    require("AppDataPathProfile.Client" not in (root / "src/apps/Bakabase.App/Program.cs").read_text(encoding="utf-8"),
            "The unified application must not select client AppData")
    projects, packages = project_graph(root / "src/apps/Bakabase.Service/Bakabase.Service.csproj")
    require(not any(name.startswith(("Bakabase.Client", "Bakabase.Remoting", "Bakabase.Shell")) for name in projects),
            "Service transitively references a desktop/client host or the relay")
    require(not any(name.startswith(("Avalonia", "Yarp")) for name in packages), "Service transitively references desktop/client packages")
    projects, packages = project_graph(root / "src/apps/Bakabase.Remoting/Bakabase.Remoting.csproj")
    require(not any(name in ("Bakabase.Service", "Bakabase.Modules.Federation") or name.startswith(("Bakabase.Shell", "Bakabase.Client"))
                    for name in projects),
            "The relay references a server host, the federation module, the shell or a client product")
    require(not any(name.startswith("Avalonia") for name in packages), "The relay references desktop UI packages")
    projects, _ = project_graph(root / "src/apps/Bakabase.Client.App/Bakabase.Client.App.csproj")
    require("Bakabase.Service" not in projects and "Bakabase.Modules.Federation" not in projects,
            "Legacy client unexpectedly includes an authoritative library host")
    check_shell_graph(*project_graph(root / "src/apps/Bakabase.Shell/Bakabase.Shell.csproj"))
    check_unified_graph(project_graph(root / "src/apps/Bakabase.App/Bakabase.App.csproj")[0])
    return {"sourceIdentityChecks": "passed", "products": PRODUCTS}


def check_shell_graph(projects, packages):
    """The shell is shared by both desktop products and talks to its host only through IShellHost and
    the optional contracts in Bakabase.Abstractions (IMainViewSwitcher among them): it never references a
    host — the Service, the relay, or a client product layer — nor the relay's proxy."""
    hosts = sorted(name for name in projects
                   if name in ("Bakabase.Service", "Bakabase.Modules.Federation")
                   or name.startswith(("Bakabase.Remoting", "Bakabase.Client")))
    require(not hosts, f"The shell references a host or the relay {hosts}")
    require(not any(name.startswith("Yarp") for name in packages), "The shell references the relay's proxy")


def check_unified_graph(projects):
    """The unified app composes the relay (Bakabase.Remoting) but never the retired thin client's
    product layer: Bakabase.Client.Remoting, or anything else named Bakabase.Client*."""
    require("Bakabase.Remoting" in projects, "The unified application no longer composes the relay")
    thin_client = sorted(name for name in projects if name.startswith("Bakabase.Client"))
    require(not thin_client, f"The unified application references thin-client projects {thin_client}")


def check_publish(directory, role, require_web=False):
    require(directory.is_dir(), f"Publish directory missing: {directory}")
    names = {path.name for path in directory.rglob("*.dll")}
    dependencies = set()
    manifests = list(directory.glob("*.deps.json"))
    require(bool(manifests), "No .deps.json found; check an actual dotnet publish output")
    for path in manifests:
        dependencies.update(json.loads(path.read_text(encoding="utf-8-sig")).get("libraries", {}))
    required = {"server": {"Bakabase.Service.dll", "Bakabase.Modules.Federation.dll"},
                "unified": {"Bakabase.dll", "Bakabase.Shell.dll", "Bakabase.Service.dll", "Bakabase.Modules.Federation.dll",
                            "Bakabase.Remoting.dll"},
                "client": {"Bakabase.Client.dll", "Bakabase.Client.Remoting.dll", "Bakabase.Remoting.dll", "Bakabase.Shell.dll"}}[role]
    # The unified app ships the relay, and YARP only as the relay's own dependency: YARP in a
    # package without Bakabase.Remoting.dll came from somewhere it does not belong. Checked
    # before the required set so that case is reported as what it is.
    forbidden = {"server": ("Bakabase.Client", "Bakabase.Remoting", "Bakabase.Shell", "Avalonia", "Yarp"),
                 "unified": ("Bakabase.Client",) + (() if "Bakabase.Remoting.dll" in names else ("Yarp",)),
                 "client": ("Bakabase.Service", "Bakabase.Modules.Federation", "Bakabase.Migrations")}[role]
    require(not any(name.startswith(forbidden) for name in names | dependencies),
            f"{role}: forbidden shipped dependency {sorted(name for name in names | dependencies if name.startswith(forbidden))}")
    require(required <= names, f"{role}: missing required assemblies {sorted(required - names)}")
    if role == "client":
        require(not (directory / "web").exists(), "Legacy client must not ship the local frontend")
    if require_web:
        require(role != "client" and (directory / "web/index.html").is_file(), "Unified/server release requires the actual local frontend")
        require(any((directory / "web").rglob("*.js")), "Frontend directory has no built JavaScript")
    return {"role": role, "publish": str(directory), "assemblies": sorted(names),
            "dependencyCount": len(dependencies), "localFrontendChecked": require_web, "passed": True}


def check_macos_portable(archive, role, version):
    require(role in PRODUCTS, "Only desktop products have macOS bundles")
    product = PRODUCTS[role]
    with zipfile.ZipFile(archive) as package:
        plists = [name for name in package.namelist() if re.fullmatch(r"[^/]+\.app/Contents/Info.plist", name)]
        require(len(plists) == 1, "Portable archive needs exactly one application bundle")
        info = plistlib.loads(package.read(plists[0]))
        require(info.get("CFBundleIdentifier") == product["bundle"], "Packaged macOS identity changed")
        require(info.get("CFBundleExecutable") == product["assembly"], "Packaged macOS executable missing or incorrect")
        require(info.get("CFBundlePackageType") == "APPL", "Portable bundle is not an application")
        core = version.split("-", 1)[0].split("+", 1)[0]
        require(info.get("CFBundleVersion") == core and info.get("CFBundleShortVersionString") == core,
                "Packaged macOS version differs from the release")
        require(info.get("CFBundleGetInfoString", "").endswith(" " + version), "Full release version missing from bundle")
        executable = plists[0].removesuffix("Info.plist") + "MacOS/" + product["assembly"]
        require(executable in package.namelist(), "Bundle executable is absent")
        entry = package.getinfo(executable)
        require((entry.external_attr >> 16) & 0o111, "Bundle executable has no execution permission")
        with package.open(entry) as stream:
            require(stream.read(4) in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"),
                    "Bundle executable is not a Mach-O binary")
    return {"archive": str(archive), "role": role, "version": version, "passed": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--publish-dir", type=Path)
    parser.add_argument("--role", choices=("server", "unified", "client"))
    parser.add_argument("--require-web", action="store_true")
    parser.add_argument("--macos-portable-dir", type=Path)
    parser.add_argument("--version")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    if bool(args.publish_dir) != bool(args.role):
        parser.error("--publish-dir and --role must be supplied together")
    if args.macos_portable_dir and (args.role not in PRODUCTS or not args.version):
        parser.error("--macos-portable-dir requires a desktop --role and --version")
    try:
        report = check_sources(args.root)
        if args.publish_dir:
            report["publish"] = check_publish(args.publish_dir, args.role, args.require_web)
        if args.macos_portable_dir:
            archives = list(args.macos_portable_dir.glob("*-Portable.zip"))
            require(len(archives) == 1, "Expected exactly one macOS portable archive")
            report["macosPortable"] = check_macos_portable(archives[0], args.role, args.version)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))
        return 0
    except (AssertionError, OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"RELEASE CONTRACT FAILED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
