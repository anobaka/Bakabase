#!/usr/bin/env python3
"""Execute real identity/AppData/relocation/legacy migration-export contracts; no GUI/feed operations."""
import argparse
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
CLASSES = (
    "Bakabase.Tests.AppDataProfileTests",
    "Bakabase.Tests.DefaultAppDataPathResolverTests",
    "Bakabase.Tests.AppDataPathRelocationTests",
    "Bakabase.Tests.DataPathValidatorTests",
    "Bakabase.Tests.LegacyInstallDetectorTests",
    "Bakabase.Tests.Relocation.PendingRelocationRunnerTests",
    "Bakabase.Tests.RemoteAccess.ClientUpdateSourceTests",
    "Bakabase.Tests.RemoteAccess.ClientPipelineTests",
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--results-directory", type=Path)
    args = parser.parse_args()
    results = args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-upgrade-contracts-"))
    results.mkdir(parents=True, exist_ok=True)
    source = subprocess.run([sys.executable, str(Path(__file__).with_name("check-release-contract.py")),
                             "--report", str(results / "product-identities.json")], cwd=ROOT)
    if source.returncode:
        return source.returncode
    command = [sys.executable, str(ROOT / "src/tests/run-backend-tests.py"), "--dotnet", args.dotnet,
               "--project", str(ROOT / "src/tests/Bakabase.Tests/Bakabase.Tests.csproj"),
               "--results-directory", str(results)]
    if args.no_build:
        command.append("--no-build")
    for name in CLASSES:
        command += ["--class", name]
    return subprocess.run(command, cwd=ROOT).returncode


if __name__ == "__main__":
    sys.exit(main())
