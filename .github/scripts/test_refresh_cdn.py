"""Simulate release cache contents and Alibaba batch refresh responses."""

from contextlib import redirect_stdout
import io
import unittest
from unittest.mock import Mock, patch

import refresh_cdn as cdn


def rows_for(requests, status="Complete"):
    return [
        {"ObjectType": kind.lower(), "ObjectPath": path, "Status": status}
        for kind, paths in requests for path in paths
    ]


def response(rows, total=None):
    return {"Tasks": {"CDNTask": rows}, "TotalCount": len(rows) if total is None else total}


class RefreshPlanTests(unittest.TestCase):
    def test_vpk_120_stable_output_and_same_version_recovery(self):
        requests = cdn.refresh_requests("2.4.0")
        files = set(requests[0][1])
        for platform, channel, extension, legacy, suffix in (
            ("win-x64", "win", "exe", "RELEASES", ""),
            ("osx-x64", "osx", "pkg", "RELEASES-osx", "-osx"),
            ("osx-arm64", "osx", "pkg", "RELEASES-osx", "-osx"),
        ):
            prefix = f"{cdn.BASE_URL}/releases/{platform}"
            expected = {
                f"{prefix}/releases.{channel}.json",
                f"{prefix}/assets.{channel}.json",
                f"{prefix}/{legacy}",
                f"{prefix}/Bakabase-{channel}-Setup.{extension}",
                f"{prefix}/Bakabase-{channel}-Portable.zip",
                f"{prefix}/Bakabase-2.4.0{suffix}-full.nupkg",
                f"{prefix}/Bakabase-2.4.0{suffix}-delta.nupkg",
            }
            self.assertTrue(expected <= files)
        self.assertIn(f"{cdn.BASE_URL}/releases/changelogs/2.4.0/README.md", files)
        self.assertIn(f"{cdn.BASE_URL}/releases/changelogs/index.json", files)
        self.assertIn(f"{cdn.BASE_URL}/scripts/bakabase.user.js", files)
        self.assertEqual(("Directory", [f"{cdn.BASE_URL}/archives/2.4.0/"]), requests[1])
        self.assertEqual(24, len(files))

    def test_beta_and_rc_match_build_channel_and_keep_other_channel_cached(self):
        for version in ("2.4.0-beta.141", "2.4.0-rc.2", "2.4.0-RC.3"):
            with self.subTest(version=version):
                files = set(cdn.refresh_requests(version)[0][1])
                for platform in cdn.PLATFORMS:
                    prefix = f"{cdn.BASE_URL}/releases/{platform}"
                    extension = "exe" if platform.startswith("win-") else "pkg"
                    self.assertIn(f"{prefix}/releases.beta.json", files)
                    self.assertIn(f"{prefix}/assets.beta.json", files)
                    self.assertIn(f"{prefix}/RELEASES-beta", files)
                    self.assertIn(f"{prefix}/Bakabase-beta-Setup.{extension}", files)
                    self.assertIn(f"{prefix}/Bakabase-beta-Portable.zip", files)
                    self.assertIn(f"{prefix}/Bakabase-{version}-beta-full.nupkg", files)
                    self.assertNotIn(f"{prefix}/releases.win.json", files)
                    self.assertNotIn(f"{prefix}/releases.osx.json", files)

    def test_package_version_normalization_and_metadata_channel_logic(self):
        files = set(cdn.refresh_requests("02.04.00+beta.build")[0][1])
        self.assertIn(f"{cdn.BASE_URL}/releases/win-x64/Bakabase-2.4.0-beta-full.nupkg", files)
        self.assertIn(f"{cdn.BASE_URL}/releases/changelogs/02.04.00+beta.build/README.md", files)

    def test_historical_packages_changelogs_and_archives_remain_cached(self):
        requests = cdn.refresh_requests("2.4.0-beta.141")
        old_cache = {
            f"{cdn.BASE_URL}/releases/win-x64/Bakabase-2.4.0-beta.140-beta-full.nupkg",
            f"{cdn.BASE_URL}/releases/osx-x64/Bakabase-2.3.0-osx-full.nupkg",
            f"{cdn.BASE_URL}/releases/changelogs/2.4.0-beta.140/README.md",
            f"{cdn.BASE_URL}/archives/2.4.0-beta.140/win-x64/Bakabase-2.4.0-beta.140-win-x64-Setup.exe",
        }
        current_cache = {
            f"{cdn.BASE_URL}/releases/win-x64/Bakabase-2.4.0-beta.141-beta-full.nupkg",
            f"{cdn.BASE_URL}/releases/changelogs/2.4.0-beta.141/README.md",
            f"{cdn.BASE_URL}/archives/2.4.0-beta.141/win-x64/Bakabase-2.4.0-beta.141-win-x64-Setup.exe",
            f"{cdn.BASE_URL}/releases/win-x64/releases.beta.json?arch=X64&os=win&localVersion=2.4.0-beta.140",
        }
        cached = old_cache | current_cache
        for kind, paths in requests:
            for path in paths:
                if kind == "File":
                    # Live domain set_hashkey_args.disable=on makes updater
                    # query variants share the same cache key as the bare URL.
                    cached = {url for url in cached if url.split("?", 1)[0] != path}
                else:
                    cached = {url for url in cached if not url.startswith(path)}
        self.assertEqual(old_cache, cached)

    def test_invalid_version_does_not_submit_any_requests(self):
        for version in ("", "v2.4.0", "2.4.0/../", "2.4.0\nhttps://other.example/"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                cdn.refresh_requests(version)


class RefreshTaskTests(unittest.TestCase):
    def setUp(self):
        self.now = 0
        self.requests = cdn.refresh_requests("2.4.0-beta.141")
        self.rows = rows_for(self.requests)
        self.addCleanup(patch.stopall)
        patch.object(cdn.time, "monotonic", side_effect=lambda: self.now).start()
        self.sleep = patch.object(cdn.time, "sleep", side_effect=self.advance).start()
        redirect = redirect_stdout(io.StringIO())
        redirect.__enter__()
        self.addCleanup(redirect.__exit__, None, None, None)

    def advance(self, seconds):
        self.now += seconds

    def run_refresh(self, call):
        cdn.refresh("2.4.0-beta.141", call, timeout=20, interval=10)

    def test_multiple_ids_and_same_second_merged_ids_are_all_polled(self):
        call = Mock(side_effect=[
            {"RefreshTaskId": "101, 102"},
            {"RefreshTaskId": "102,103"},
            response(self.rows[:12]), response(self.rows[12:24]), response(self.rows[24:]),
        ])
        self.run_refresh(call)
        self.assertEqual(["101", "102", "103"], [x.kwargs["TaskId"] for x in call.call_args_list[2:]])
        self.assertEqual("File", call.call_args_list[0].kwargs["ObjectType"])
        self.assertEqual("Directory", call.call_args_list[1].kwargs["ObjectType"])
        self.assertEqual("\n".join(self.requests[0][1]), call.call_args_list[0].kwargs["ObjectPath"])
        self.sleep.assert_not_called()

    def test_does_not_finish_when_only_the_first_object_is_complete(self):
        partial = [dict(x) for x in self.rows]
        partial[-1]["Status"] = "Refreshing"
        call = Mock(side_effect=[
            {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"},
            response(partial), response(self.rows),
        ])
        self.run_refresh(call)
        self.sleep.assert_called_once_with(10)

    def test_unrelated_merged_refresh_failures_or_pending_rows_do_not_block(self):
        for status in ("Failed", "Refreshing"):
            with self.subTest(status=status):
                unrelated = {
                    "ObjectType": "file",
                    "ObjectPath": "https://cdn-public.anobaka.com/app/bakabase-mobile/manifest.json",
                    "Status": status,
                }
                call = Mock(side_effect=[
                    {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"},
                    response(self.rows + [unrelated]),
                ])
                self.run_refresh(call)
                self.sleep.assert_not_called()

    def test_eventual_visibility_waits_for_missing_objects(self):
        call = Mock(side_effect=[
            {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"},
            response([]), response(self.rows[:1]), response(self.rows),
        ])
        self.run_refresh(call)
        self.assertEqual(2, self.sleep.call_count)

    def test_pagination_checks_a_failure_after_the_first_page(self):
        other_rows = [{"ObjectType": "file", "ObjectPath": f"https://other/{i}", "Status": "Complete"}
                      for i in range(76)]
        first_page = self.rows[:24] + other_rows
        last_page = [dict(self.rows[-1], Status="Failed")]
        call = Mock(side_effect=[
            {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"},
            response(first_page, total=101), response(last_page, total=101),
        ])
        with self.assertRaisesRegex(RuntimeError, "101 Failed"):
            self.run_refresh(call)
        self.assertEqual(2, call.call_args_list[-1].kwargs["PageNumber"])

    def test_incomplete_pagination_snapshot_cannot_finish(self):
        call = Mock(side_effect=[
            {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"},
            response(self.rows, total=26), response([], total=26), response(self.rows),
        ])
        self.run_refresh(call)
        self.sleep.assert_called_once_with(10)

    def test_terminal_failures_from_later_objects_are_reported(self):
        for status in ("Failed", "Timeout", "Canceled"):
            with self.subTest(status=status):
                rows = [dict(x) for x in self.rows]
                rows[-1]["Status"] = status
                call = Mock(side_effect=[
                    {"RefreshTaskId": "101"}, {"RefreshTaskId": "101"}, response(rows),
                ])
                with self.assertRaisesRegex(RuntimeError, status):
                    self.run_refresh(call)

    def test_missing_tasks_time_out_instead_of_succeeding(self):
        call = Mock(side_effect=lambda action, **kwargs: (
            {"RefreshTaskId": "101"} if action == "RefreshObjectCaches" else response([])
        ))
        with self.assertRaisesRegex(RuntimeError, "timed out after 20s"):
            self.run_refresh(call)
        self.assertEqual(20, self.now)

    def test_missing_or_invalid_ids_fail_before_polling(self):
        for value in (None, "", "null", "101,", "101,bad"):
            with self.subTest(value=value), self.assertRaisesRegex(RuntimeError, "valid refresh task IDs"):
                self.run_refresh(Mock(return_value={"RefreshTaskId": value}))


if __name__ == "__main__":
    unittest.main()
