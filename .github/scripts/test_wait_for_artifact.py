"""Exercise artifact arrival, rerun isolation and bounded API failure handling."""

from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, urlsplit

import wait_for_artifact as waiter


def response(*artifacts):
    return io.BytesIO(json.dumps({"artifacts": list(artifacts)}).encode())


def artifact(name="fe-2", artifact_id=123, expired=False):
    return {"name": name, "id": artifact_id, "expired": expired}


class ArtifactWaitTests(unittest.TestCase):
    def setUp(self):
        self.now = 0
        self.sleeps = []
        self.output = io.StringIO()
        self.environment = {
            "GITHUB_TOKEN": "private-token-that-must-not-be-printed",
            "GITHUB_API_URL": "https://api.github.com",
            "GITHUB_REPOSITORY": "example/repository",
            "GITHUB_RUN_ID": "987",
        }
        self.addCleanup(patch.stopall)
        patch.dict(waiter.os.environ, self.environment, clear=True).start()
        patch.object(waiter.time, "monotonic", side_effect=lambda: self.now).start()
        patch.object(waiter.time, "sleep", side_effect=self.sleep).start()
        self.urlopen = patch.object(waiter, "urlopen").start()
        self.redirect = redirect_stdout(self.output)
        self.redirect.__enter__()
        self.addCleanup(self.redirect.__exit__, None, None, None)

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now += seconds

    def test_waits_for_current_attempt_and_ignores_expired_or_other_names(self):
        self.urlopen.side_effect = [
            response(artifact(name="fe-1"), artifact(expired=True), artifact(name="fe-20")),
            response(),
            response(artifact(artifact_id=456)),
        ]
        self.assertEqual(456, waiter.wait_for_artifact("fe-2", timeout=30, interval=10))
        self.assertEqual([10, 10], self.sleeps)
        self.assertEqual(3, self.urlopen.call_count)
        request = self.urlopen.call_args.args[0]
        self.assertEqual("/repos/example/repository/actions/runs/987/artifacts", urlsplit(request.full_url).path)
        self.assertEqual(["fe-2"], parse_qs(urlsplit(request.full_url).query)["name"])
        self.assertEqual(f"Bearer {self.environment['GITHUB_TOKEN']}", request.get_header("Authorization"))
        self.assertNotIn(self.environment["GITHUB_TOKEN"], self.output.getvalue())

    def test_missing_artifact_times_out_without_oversleep_or_extra_request(self):
        self.urlopen.side_effect = lambda *args, **kwargs: response()
        with self.assertRaisesRegex(TimeoutError, "within 25 seconds"):
            waiter.wait_for_artifact("fe-2", timeout=25, interval=10)
        self.assertEqual([10, 10, 5], self.sleeps)
        self.assertEqual(25, self.now)
        self.assertEqual([25, 15, 5], [call.kwargs["timeout"] for call in self.urlopen.call_args_list])

    def test_authentication_and_other_permanent_http_errors_fail_immediately(self):
        for status in (401, 403, 404):
            with self.subTest(status=status):
                self.urlopen.side_effect = HTTPError("https://api.github.com", status, "denied", {}, None)
                with self.assertRaisesRegex(RuntimeError, f"HTTP {status}"):
                    waiter.wait_for_artifact("fe-2")
                self.assertEqual([], self.sleeps)

    def test_transient_http_and_network_errors_retry_then_succeed(self):
        self.urlopen.side_effect = [
            HTTPError("https://api.github.com", 503, "unavailable", {}, None),
            URLError("connection interrupted"),
            TimeoutError("socket stalled"),
            response(artifact()),
        ]
        self.assertEqual(123, waiter.wait_for_artifact("fe-2", interval=2))
        self.assertEqual([2, 2, 2], self.sleeps)

    def test_retry_after_respects_the_remaining_time_budget(self):
        self.urlopen.side_effect = HTTPError("https://api.github.com", 429, "throttled", {"Retry-After": "120"}, None)
        with self.assertRaises(TimeoutError):
            waiter.wait_for_artifact("fe-2", timeout=25, interval=10)
        self.assertEqual([25], self.sleeps)
        self.assertEqual(1, self.urlopen.call_count)

    def test_network_request_time_counts_towards_the_deadline(self):
        def stalled_request(*args, **kwargs):
            self.now += kwargs["timeout"]
            raise URLError("socket stalled")

        self.urlopen.side_effect = stalled_request
        with self.assertRaises(TimeoutError):
            waiter.wait_for_artifact("fe-2", timeout=35, interval=10)
        self.assertEqual(35, self.now)
        self.assertEqual([5], self.sleeps)
        self.assertEqual(1, self.urlopen.call_count)

    def test_invalid_or_ambiguous_artifact_ids_fail_without_retrying(self):
        for artifacts in ([artifact(artifact_id="123")], [artifact(artifact_id=True)], [artifact(), artifact(artifact_id=456)]):
            with self.subTest(artifacts=artifacts):
                self.urlopen.side_effect = lambda *args, **kwargs: response(*artifacts)
                with self.assertRaises(ValueError):
                    waiter.wait_for_artifact("fe-2")
                self.assertEqual([], self.sleeps)

    def test_writes_exact_artifact_id_to_output_without_overwriting_existing_outputs(self):
        self.urlopen.return_value = response(artifact(artifact_id=456))
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            output.write_text("existing=value\n", encoding="utf-8")
            with patch.dict(waiter.os.environ, {"GITHUB_OUTPUT": str(output)}):
                self.assertEqual(0, waiter.main(["--name", "fe-2"]))
            self.assertEqual("existing=value\nartifact-id=456\n", output.read_text(encoding="utf-8"))

    def test_timeout_does_not_emit_an_artifact_output(self):
        self.urlopen.side_effect = lambda *args, **kwargs: response()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "output"
            with patch.dict(waiter.os.environ, {"GITHUB_OUTPUT": str(output)}):
                with self.assertRaises(TimeoutError):
                    waiter.main(["--name", "fe-2", "--timeout", "1"])
            self.assertFalse(output.exists())

    def test_failed_job_rerun_can_reuse_latest_successful_attempt_from_same_run(self):
        self.urlopen.return_value = response(
            artifact(name="fe-1", artifact_id=111),
            artifact(name="fe-2", artifact_id=222),
            artifact(name="fe-3", artifact_id=333, expired=True),
            artifact(name="fe-4", artifact_id=444),
            artifact(name="other-fe-3", artifact_id=555),
        )
        self.assertEqual(222, waiter.wait_for_artifact("fe-3", previous_attempts=True))
        request = self.urlopen.call_args.args[0]
        self.assertEqual("/repos/example/repository/actions/runs/987/artifacts", urlsplit(request.full_url).path)
        self.assertNotIn("name", parse_qs(urlsplit(request.full_url).query))
        self.assertEqual([], self.sleeps)

    def test_current_attempt_is_preferred_over_previous_attempts_regardless_of_order(self):
        self.urlopen.return_value = response(artifact(name="fe-3", artifact_id=333), artifact(name="fe-2", artifact_id=222))
        self.assertEqual(333, waiter.wait_for_artifact("fe-3", previous_attempts=True))

    def test_previous_attempt_lookup_checks_all_pages(self):
        first_page = [artifact(name="fe-1", artifact_id=111)]
        first_page.extend(artifact(name=f"unrelated-{number}", artifact_id=1000 + number) for number in range(99))
        self.urlopen.side_effect = [response(*first_page), response(artifact(name="fe-2", artifact_id=222))]
        self.assertEqual(222, waiter.wait_for_artifact("fe-3", previous_attempts=True))
        self.assertEqual(["1", "2"], [parse_qs(urlsplit(call.args[0].full_url).query)["page"][0]
                                       for call in self.urlopen.call_args_list])

    def test_previous_attempt_lookup_requires_an_attempt_number(self):
        with self.assertRaisesRegex(ValueError, "run_attempt"):
            waiter.wait_for_artifact("fe", previous_attempts=True)
        self.urlopen.assert_not_called()


if __name__ == "__main__":
    unittest.main()
