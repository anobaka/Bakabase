"""Failure-path checks for the publish gate, using intentionally incomplete packages."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("release_contract", Path(__file__).with_name("check-release-contract.py"))
contract = importlib.util.module_from_spec(spec)
spec.loader.exec_module(contract)


class PublishContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        for name in ("Bakabase.Service.dll", "Bakabase.Modules.Federation.dll"):
            (self.directory / name).touch()
        self.manifest = self.directory / "Bakabase.Service.deps.json"
        self.manifest.write_text(json.dumps({"libraries": {"Bakabase.Service/1.0": {}}}))

    def tearDown(self):
        self.temporary.cleanup()

    def test_server_without_desktop_or_client_dependencies(self):
        self.assertTrue(contract.check_publish(self.directory, "server")["passed"])

    def test_transitive_desktop_dependency_is_rejected_even_without_its_dll(self):
        self.manifest.write_text(json.dumps({"libraries": {"Avalonia/11.3.20": {}}}))
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "server")

    def test_nested_client_assembly_is_rejected(self):
        nested = self.directory / "plugins"
        nested.mkdir()
        (nested / "Bakabase.Client.Remoting.dll").touch()
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "server")

    def test_missing_dependency_manifest_is_not_a_publish_success(self):
        self.manifest.unlink()
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "server")

    def test_stub_frontend_is_not_a_shippable_frontend(self):
        web = self.directory / "web"
        web.mkdir()
        (web / "index.html").write_text("stub")
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "server", require_web=True)
        (web / "app.js").write_text("/* built fixture */")
        self.assertTrue(contract.check_publish(self.directory, "server", require_web=True)["passed"])

    def test_client_cannot_carry_frontend_or_server(self):
        for name in ("Bakabase.Client.dll", "Bakabase.Client.Remoting.dll", "Bakabase.Shell.dll"):
            (self.directory / name).touch()
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "client")


if __name__ == "__main__":
    unittest.main()
