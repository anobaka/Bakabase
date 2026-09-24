"""Failure-path checks for the publish gate, using intentionally incomplete packages."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import plistlib
import zipfile

spec = importlib.util.spec_from_file_location("release_contract", Path(__file__).resolve().parents[2] / "scripts/check-release-contract.py")
contract = importlib.util.module_from_spec(spec)
spec.loader.exec_module(contract)
prepare_spec = importlib.util.spec_from_file_location("prepare_plist", Path(__file__).resolve().parents[2] / "scripts/prepare-macos-plist.py")
prepare_plist = importlib.util.module_from_spec(prepare_spec)
prepare_spec.loader.exec_module(prepare_plist)


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

    def test_relay_is_never_part_of_the_server(self):
        (self.directory / "Bakabase.Remoting.dll").touch()
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "server")

    def test_client_cannot_carry_frontend_or_server(self):
        for name in ("Bakabase.Client.dll", "Bakabase.Client.Remoting.dll", "Bakabase.Remoting.dll", "Bakabase.Shell.dll"):
            (self.directory / name).touch()
        with self.assertRaises(AssertionError):
            contract.check_publish(self.directory, "client")


class UnifiedPublishContractTests(unittest.TestCase):
    """The unified app ships its own server and the relay it manages other servers through,
    and nothing of the retired thin client."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        for name in ("Bakabase.dll", "Bakabase.Shell.dll", "Bakabase.Service.dll", "Bakabase.Modules.Federation.dll",
                     "Bakabase.Remoting.dll", "Yarp.ReverseProxy.dll", "Avalonia.dll"):
            (self.directory / name).touch()
        self.manifest = self.directory / "Bakabase.deps.json"
        self.manifest.write_text(json.dumps({"libraries": {
            "Bakabase/1.0": {}, "Bakabase.Remoting/1.0": {}, "Yarp.ReverseProxy/2.3.0": {}, "Avalonia/11.3.20": {}}}))

    def tearDown(self):
        self.temporary.cleanup()

    def test_unified_with_relay_and_its_proxy_passes(self):
        self.assertTrue(contract.check_publish(self.directory, "unified")["passed"])

    def test_unified_without_relay_is_rejected(self):
        (self.directory / "Bakabase.Remoting.dll").unlink()
        (self.directory / "Yarp.ReverseProxy.dll").unlink()
        self.manifest.write_text(json.dumps({"libraries": {"Bakabase/1.0": {}}}))
        with self.assertRaisesRegex(AssertionError, "Bakabase.Remoting.dll"):
            contract.check_publish(self.directory, "unified")

    def test_proxy_without_relay_is_rejected(self):
        (self.directory / "Bakabase.Remoting.dll").unlink()
        with self.assertRaisesRegex(AssertionError, "forbidden shipped dependency.*Yarp"):
            contract.check_publish(self.directory, "unified")

    def test_thin_client_product_layer_is_rejected(self):
        (self.directory / "Bakabase.Client.Remoting.dll").touch()
        with self.assertRaisesRegex(AssertionError, "Bakabase.Client.Remoting"):
            contract.check_publish(self.directory, "unified")

    def test_thin_client_dependency_in_manifest_is_rejected(self):
        self.manifest.write_text(json.dumps({"libraries": {"Bakabase.Client.Remoting/1.0": {}}}))
        with self.assertRaisesRegex(AssertionError, "forbidden shipped dependency"):
            contract.check_publish(self.directory, "unified")


class UnifiedProjectGraphTests(unittest.TestCase):
    def test_actual_unified_graph_composes_the_relay_and_no_thin_client(self):
        projects, packages = contract.project_graph(contract.ROOT / "src/apps/Bakabase.App/Bakabase.App.csproj")
        contract.check_unified_graph(projects)
        self.assertIn("Bakabase.Remoting", projects)
        self.assertIn("Yarp.ReverseProxy", packages)

    def test_thin_client_project_in_unified_graph_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "thin-client"):
            contract.check_unified_graph({"Bakabase.App", "Bakabase.Remoting", "Bakabase.Client.Remoting"})

    def test_unified_graph_without_relay_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "relay"):
            contract.check_unified_graph({"Bakabase.App", "Bakabase.Service"})


class ShellProjectGraphTests(unittest.TestCase):
    """The shell reaches its host only through contracts, so it can sit in front of either product."""

    def test_actual_shell_graph_references_no_host(self):
        contract.check_shell_graph(*contract.project_graph(contract.ROOT / "src/apps/Bakabase.Shell/Bakabase.Shell.csproj"))

    def test_shell_referencing_the_service_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Bakabase.Service"):
            contract.check_shell_graph({"Bakabase.Shell", "Bakabase.Service"}, set())

    def test_shell_referencing_the_relay_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Bakabase.Remoting"):
            contract.check_shell_graph({"Bakabase.Shell", "Bakabase.Remoting"}, set())

    def test_shell_referencing_a_client_product_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "Bakabase.Client.Remoting"):
            contract.check_shell_graph({"Bakabase.Shell", "Bakabase.Client.Remoting"}, set())

    def test_shell_with_the_proxy_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "proxy"):
            contract.check_shell_graph({"Bakabase.Shell"}, {"Yarp.ReverseProxy"})


class MacPortableContractTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        self.info = {
            "CFBundleIdentifier": "com.anobaka.bakabase", "CFBundleExecutable": "Bakabase",
            "CFBundleDisplayName": "Bakabase", "CFBundlePackageType": "APPL",
            "CFBundleVersion": "2.4.0", "CFBundleShortVersionString": "2.4.0",
            "CFBundleGetInfoString": "Bakabase 2.4.0-beta.3",
        }

    def tearDown(self):
        self.temporary.cleanup()

    def archive(self, executable=True, mode=0o755):
        path = self.directory / "portable.zip"
        with zipfile.ZipFile(path, "w") as package:
            package.writestr("Bakabase.app/Contents/Info.plist", plistlib.dumps(self.info))
            if executable:
                entry = zipfile.ZipInfo("Bakabase.app/Contents/MacOS/Bakabase")
                entry.external_attr = (0o100000 | mode) << 16
                package.writestr(entry, b"\xcf\xfa\xed\xfe")
        return path

    def check(self, **kwargs):
        return contract.check_macos_portable(self.archive(**kwargs), "unified", "2.4.0-beta.3")

    def test_valid_bundle_metadata_and_executable(self):
        self.assertTrue(self.check()["passed"])

    def test_custom_plist_without_executable_is_rejected(self):
        del self.info["CFBundleExecutable"]
        with self.assertRaisesRegex(AssertionError, "executable missing"):
            self.check()

    def test_wrong_product_is_rejected(self):
        self.info["CFBundleIdentifier"] = "com.anobaka.bakabase.client"
        with self.assertRaisesRegex(AssertionError, "identity"):
            self.check()

    def test_static_template_version_is_rejected(self):
        self.info["CFBundleVersion"] = "1.0.0"
        with self.assertRaisesRegex(AssertionError, "version"):
            self.check()

    def test_missing_executable_file_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "absent"):
            self.check(executable=False)

    def test_executable_permission_is_required(self):
        with self.assertRaisesRegex(AssertionError, "permission"):
            self.check(mode=0o644)

    def test_prepare_supports_both_products_and_preserves_identity(self):
        for role, product in contract.PRODUCTS.items():
            template = contract.ROOT / "src/apps" / product["project"] / "Info.plist"
            before = template.read_bytes()
            output = self.directory / (role + ".plist")
            prepare_plist.prepare(template, output, "2.4.0-beta.3+abc123")
            info = plistlib.loads(output.read_bytes())
            self.assertEqual(product["bundle"], info["CFBundleIdentifier"])
            self.assertEqual(product["assembly"], info["CFBundleExecutable"])
            self.assertEqual("2.4.0", info["CFBundleVersion"])
            self.assertTrue(info["CFBundleGetInfoString"].endswith(" 2.4.0-beta.3+abc123"))
            self.assertEqual(before, template.read_bytes())

    def test_invalid_release_does_not_modify_output(self):
        output = self.directory / "existing.plist"
        output.write_bytes(b"unchanged")
        with self.assertRaises(ValueError):
            prepare_plist.prepare(output, output, "not-a-release")
        self.assertEqual(b"unchanged", output.read_bytes())


if __name__ == "__main__":
    unittest.main()
