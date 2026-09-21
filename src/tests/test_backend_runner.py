"""Selection guard regressions; no .NET process or fixture is started."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("backend_runner", Path(__file__).with_name("run-backend-tests.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class BackendRunnerSelectionTests(unittest.TestCase):
    classes = [
        "Bakabase.Tests.Federation.PlayerTests",
        "Bakabase.Tests.Federation.Security.FutureTests",
        "Bakabase.Tests.FederationOther.UnrelatedTests",
        "Bakabase.Tests.RemoteAccess.ClientPipelineTests",
    ]

    def test_namespace_includes_future_child_classes_but_not_neighboring_namespaces(self):
        self.assertEqual(sorted(self.classes[:2]), runner.select_classes(
            self.classes, namespaces=["Bakabase.Tests.Federation"]))

    def test_explicit_classes_and_namespaces_form_a_deduplicated_union(self):
        self.assertEqual(sorted([*self.classes[:2], self.classes[3]]), runner.select_classes(
            self.classes, [self.classes[0], self.classes[3]], ["Bakabase.Tests.Federation"]))

    def test_no_filter_preserves_the_complete_project_suite(self):
        self.assertEqual(sorted(self.classes), runner.select_classes(self.classes))

    def test_existing_exact_class_selection_stays_exact(self):
        self.assertEqual([self.classes[0]], runner.select_classes(self.classes, [self.classes[0]]))

    def test_an_unknown_namespace_fails_even_when_an_explicit_class_matches(self):
        with self.assertRaisesRegex(RuntimeError, "namespace was not discovered"):
            runner.select_classes(self.classes, [self.classes[0]], ["Bakabase.Tests.Missing"])

    def test_an_unknown_class_fails_even_when_the_namespace_matches(self):
        with self.assertRaisesRegex(RuntimeError, "classes were not discovered"):
            runner.select_classes(self.classes, ["Bakabase.Tests.Missing"], ["Bakabase.Tests.Federation"])

    def test_namespace_without_matching_tests_cannot_produce_an_empty_success(self):
        with self.assertRaisesRegex(RuntimeError, "namespace was not discovered"):
            runner.select_classes([], namespaces=["Bakabase.Tests.Federation"])

    def test_empty_wildcard_or_partial_namespace_is_rejected(self):
        for namespace in ("", "*", "Bakabase.Tests.Federation.", "Bakabase..Tests"):
            with self.subTest(namespace=namespace), self.assertRaisesRegex(RuntimeError, "Invalid test namespace"):
                runner.select_classes(self.classes, namespaces=[namespace])


if __name__ == "__main__":
    unittest.main()
