#!/usr/bin/env python3
"""Execute real identity/AppData/relocation/legacy pairing-import contracts; no GUI/feed operations."""
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
    "Bakabase.Tests.RemoteAccess.Console.LegacyClientLocationTests",
    "Bakabase.Tests.RemoteAccess.Console.LegacyClientImportTests",
    "Bakabase.Tests.RemoteAccess.Console.RelayPipelineTests",
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--results-directory", type=Path)
    args = parser.parse_args()
    results = args.results_directory or Path(tempfile.mkdtemp(prefix="bakabase-upgrade-contracts-"))
    results.mkdir(parents=True, exist_ok=True)
    source = subprocess.run([sys.executable, str(ROOT / "src/scripts/check-release-contract.py"),
                             "--report", str(results / "product-identities.json")], cwd=ROOT)
    if source.returncode:
        return source.returncode
    command = [args.dotnet, "run", "--project", str(ROOT / "src/tests/Bakabase.Tests/Bakabase.Tests.csproj")]
    if args.no_build:
        command.append("--no-build")
    # An exact class filter; the runner fails when nothing matches.
    command += ["--", "--minimum-expected-tests", "1", "--results-directory", str(results), "--report-trx",
                "--filter", "|".join(f"FullyQualifiedName~{name}." for name in CLASSES)]
    return subprocess.run(command, cwd=ROOT).returncode


if __name__ == "__main__":
    sys.exit(main())
