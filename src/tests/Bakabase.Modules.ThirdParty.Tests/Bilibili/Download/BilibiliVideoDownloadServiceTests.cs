using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Media;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.ThirdParty.Extensions;
using Bakabase.Modules.ThirdParty.Tests.Bilibili.Shared;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Download;

[TestClass]
public class BilibiliVideoDownloadServiceTests
{
    private const long Aid = 1001;
    private const long Cid = 5001;
    private const string Video80 = "5001-1-100026-80.m4s";
    private const string Video64 = "5001-1-100026-64.m4s";
    private const string Audio30280 = "5001-1-30280.m4s";
    private const string Video127Hevc = "5001-1-100110-127.m4s";
    private const string DurlMp4 = "25540578-1-16.mp4";

    private Harness _h = null!;

    [TestInitialize]
    public void Init() => _h = new Harness();

    [TestCleanup]
    public void Cleanup() => _h.Dispose();

    #region ResolveArchiveAsync

    [TestMethod]
    public async Task Resolve_62002_is_deleted_without_asking_pagelist()
    {
        _h.Api.On(FakeApi.View, "view-62002.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(), default);

        Assert.IsNull(r.Archive);
        Assert.AreEqual(BilibiliSkipReason.Deleted, r.Skip!.Reason);
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PageList));
        Assert.AreEqual(0, _h.Api.Count(FakeApi.MyInfo), "a deleted archive is deleted for every account");
    }

    [TestMethod]
    public async Task Resolve_access_denied_with_an_expired_login_fails_instead_of_skipping()
    {
        _h.Api.On(FakeApi.View, "view-403.json")
            .On(FakeApi.PageList, """{"code":-403,"message":"访问权限不足","ttl":1,"data":null}""")
            .On(FakeApi.MyInfo, "myinfo-101.json");

        await Assert.ThrowsExceptionAsync<BilibiliNotLoggedInException>(() =>
            _h.Service.ResolveArchiveAsync(_h.Item(), default));
    }

    [TestMethod]
    public async Task Resolve_404_with_pages_listed_is_region_restricted()
    {
        _h.Api.On(FakeApi.View, "view-404.json").On(FakeApi.PageList, "pagelist-ok.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(), default);

        Assert.AreEqual(BilibiliSkipReason.RegionRestrictedOrHidden, r.Skip!.Reason);
    }

    [TestMethod]
    public async Task Resolve_403_falls_back_to_pagelist_with_the_item_fields()
    {
        _h.Api.On(FakeApi.View, "view-403.json").On(FakeApi.PageList, "pagelist-ok.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(), default);

        Assert.IsNull(r.Skip);
        var archive = r.Archive!;
        Assert.AreEqual(Aid, archive.Aid);
        Assert.AreEqual("BV_item", archive.BvId);
        Assert.AreEqual("http://i0.hdslb.com/bfs/archive/item.png", archive.CoverUrl);
        Assert.IsNull(archive.AccessSkip);
        CollectionAssert.AreEqual(new[] {5001L, 5002L}, archive.Pages.Select(p => p.Cid).ToArray());
        CollectionAssert.AreEqual(new[] {1, 2}, archive.Pages.Select(p => p.PartNo).ToArray());
        Assert.AreEqual("第二集", archive.Pages[1].PartName);
    }

    [TestMethod]
    public async Task Resolve_follows_a_forward_once()
    {
        _h.Api.On(FakeApi.View, "view-forward.json", "view-ok-2pages.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(1008), default);

        Assert.AreEqual(1001L, r.Archive!.Aid);
        Assert.AreEqual("BV1xx411c7m1", r.Archive.BvId);
        Assert.AreEqual(2, _h.Api.Count(FakeApi.View));
        StringAssert.Contains(_h.Api.Requests.Last().Url, "aid=1001");
    }

    [TestMethod]
    public async Task Resolve_supporter_only_returns_the_archive_with_an_access_skip()
    {
        _h.Api.On(FakeApi.View, "view-upower-nopreview.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(), default);

        Assert.IsNull(r.Skip);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnly, r.Archive!.AccessSkip!.Reason);
        Assert.AreEqual(2, r.Archive.Pages.Count);
    }

    [TestMethod]
    public async Task Resolve_marks_pgc_redirects()
    {
        _h.Api.On(FakeApi.View, "view-pgc-redirect.json");

        var r = await _h.Service.ResolveArchiveAsync(_h.Item(1005), default);

        Assert.IsTrue(r.Archive!.IsPgcRedirect);
        Assert.AreEqual(3, r.Archive.Pages.Count);
    }

    #endregion

    #region Naming and existing files

    [TestMethod]
    public async Task Dash_page_is_named_from_the_legacy_request_and_downloaded()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual("1080P 高清", outcome.QualityName);
        CollectionAssert.AreEqual(new[] {"1080P 高清"}, _h.ResolvedNames.ToArray());
        // The naming request comes first, with the byte-identical legacy URL.
        var api = _h.Api.Requests.ToList();
        Assert.AreEqual(BiliBiliApiUrls.LegacyNamingPlayUrl(Aid, Cid), api[0].Url);
        Assert.IsTrue(api.FindIndex(r => r.Kind == FakeApi.PlayUrl) > 0);

        var mux = _h.Merger.Calls.Single();
        Assert.AreEqual(StubMerger.Mux, mux.Operation);
        Assert.AreEqual("v-80-c7.m4s", Path.GetFileName(mux.VideoInput));
        Assert.AreEqual("a-30280.m4s", Path.GetFileName(mux.AudioInput));
        Assert.IsFalse(mux.TagHevcAsHvc1);
        Assert.IsFalse(mux.AllowExperimentalCodecs);

        var target = _h.TargetPath("1080P 高清");
        Assert.AreEqual(target, outcome.VideoPath);
        CollectionAssert.AreEqual(FakeCdn.ContentOf(Video80).Concat(FakeCdn.ContentOf(Audio30280)).ToArray(),
            File.ReadAllBytes(target));
        Assert.IsFalse(Directory.Exists(_h.WorkDirectory()), "the work folder is removed");
        AssertProgressMonotonicEndingAt100();
    }

    [TestMethod]
    public async Task Existing_file_costs_one_request_and_is_never_reported_as_skipped()
    {
        _h.CreateTarget("1080P 高清");
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        var outcome = await _h.Service.DownloadPageAsync(
            _h.Job(access: new BilibiliSkip(BilibiliSkipReason.SupporterOnly)), default);

        Assert.AreEqual(BilibiliPageStatus.AlreadyExists, outcome.Status);
        Assert.IsNull(outcome.Skip);
        Assert.AreEqual(1, _h.Api.Requests.Count, "only the naming request");
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PlayUrl));
        Assert.AreEqual(0, _h.Api.Count(FakeApi.DmView));
        Assert.AreEqual(0, _h.Cdn.Requests.Count);
        Assert.AreEqual(0, _h.Merger.Calls.Count);
        Assert.AreEqual(100m, _h.Progress.Last());
    }

    [TestMethod]
    public async Task Access_skip_on_a_missing_file_is_reported_without_asking_for_streams()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        var outcome = await _h.Service.DownloadPageAsync(
            _h.Job(access: new BilibiliSkip(BilibiliSkipReason.SupporterOnlyPreview)), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnlyPreview, outcome.Skip!.Reason);
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PlayUrl));
        Assert.AreEqual(0, _h.Cdn.Requests.Count);
    }

    [TestMethod]
    public async Task Refused_naming_request_is_a_skip_when_no_file_exists()
    {
        _h.Api.On(FakeApi.Naming, "playurl-87008.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnly, outcome.Skip!.Reason);
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PlayUrl));
        CollectionAssert.AreEqual(BilibiliVideoDownloadService.KnownLegacyQualityNames.ToArray(),
            _h.ResolvedNames.ToArray(), "every known name was probed");
    }

    [TestMethod]
    [DataRow("4K 超高清")]
    [DataRow("1080P 高清")]
    [DataRow("360P 流畅")]
    [DataRow(null)]
    public async Task Refused_naming_request_still_finds_a_file_downloaded_earlier(string? quality)
    {
        _h.Api.On(FakeApi.Naming, "playurl-87008.json");
        _h.CreateTarget(quality);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.AlreadyExists, outcome.Status);
        Assert.AreEqual(_h.TargetPath(quality), outcome.VideoPath);
        Assert.AreEqual(quality, outcome.QualityName);
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PlayUrl));
    }

    [TestMethod]
    public async Task Unknown_naming_code_is_fatal_not_a_skip()
    {
        _h.Api.On(FakeApi.Naming, "playurl-unknown-code.json");

        var e = await Assert.ThrowsExceptionAsync<BilibiliApiException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(), default));
        BilibiliSecrets.AssertClean(e);
    }

    #endregion

    #region DASH

    [TestMethod]
    public async Task Dash_video_and_audio_download_concurrently()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        var audioAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var videoSawAudio = false;
        _h.Cdn.Route((r, ct) =>
        {
            if (FakeCdn.FileNameOf(r.RequestUri!.ToString()) == Audio30280)
            {
                audioAsked.TrySetResult();
            }

            return null;
        });
        _h.Cdn.File(Video80, (r, ct) =>
        {
            videoSawAudio = audioAsked.Task.Wait(TimeSpan.FromSeconds(10), ct);
            return Cdn.Serve(FakeCdn.ContentOf(Video80))(r, ct);
        });

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.IsTrue(videoSawAudio, "the audio request went out while the video request was open");
    }

    [TestMethod]
    public async Task Hev1_video_is_tagged_hvc1()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-8k.json");

        await _h.Service.DownloadPageAsync(_h.Job(), default);

        var mux = _h.Merger.Calls.Single();
        Assert.AreEqual("v-127-c12.m4s", Path.GetFileName(mux.VideoInput));
        Assert.IsTrue(mux.TagHevcAsHvc1);
    }

    [TestMethod]
    public async Task Hires_audio_is_muxed_as_experimental()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-hires.json");

        await _h.Service.DownloadPageAsync(_h.Job(), default);

        var mux = _h.Merger.Calls.Single();
        Assert.AreEqual("a-30251.m4s", Path.GetFileName(mux.AudioInput));
        Assert.IsTrue(mux.AllowExperimentalCodecs);
    }

    [TestMethod]
    public async Task Video_only_answer_is_muxed_without_audio()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-video-only.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var mux = _h.Merger.Calls.Single();
        Assert.IsNull(mux.AudioInput);
        Assert.IsFalse(mux.AllowExperimentalCodecs);
    }

    [TestMethod]
    public async Task Flac_mux_failure_falls_back_to_aac()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-hires.json");
        _h.Merger.Fail = c => c.AllowExperimentalCodecs;

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var calls = _h.Merger.Calls.ToList();
        Assert.AreEqual(2, calls.Count);
        Assert.AreEqual("a-30280.m4s", Path.GetFileName(calls[1].AudioInput));
        Assert.IsFalse(calls[1].AllowExperimentalCodecs);
        Assert.AreEqual("v-80-c7.m4s", Path.GetFileName(calls[1].VideoInput));
        Assert.AreEqual(1, _h.Cdn.Count(Audio30280));
    }

    [TestMethod]
    public async Task Video_mux_failure_falls_back_to_the_next_lower_avc()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        _h.Merger.Fail = c => Path.GetFileName(c.VideoInput) == "v-80-c7.m4s";

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var calls = _h.Merger.Calls.ToList();
        Assert.AreEqual(2, calls.Count);
        Assert.AreEqual("v-64-c7.m4s", Path.GetFileName(calls[1].VideoInput));
        Assert.AreEqual("a-30280.m4s", Path.GetFileName(calls[1].AudioInput));
        Assert.IsTrue(_h.Cdn.Count(Video64) > 0);
        CollectionAssert.AreEqual(FakeCdn.ContentOf(Video64).Concat(FakeCdn.ContentOf(Audio30280)).ToArray(),
            File.ReadAllBytes(outcome.VideoPath!));
    }

    [TestMethod]
    public async Task Merge_that_keeps_failing_is_a_skip_after_two_video_fallbacks()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-8k.json");
        _h.Merger.Fail = _ => true;

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.MergeFailed, outcome.Skip!.Reason);
        Assert.AreEqual(StubMerger.FailureExitCode, outcome.Skip.Code);
        // 127 HEVC (no AVC at 127) → next lower id: 125 HEVC → 120 AVC; Dolby Vision (126) is never a fallback.
        CollectionAssert.AreEqual(new[] {"v-127-c12.m4s", "v-125-c12.m4s", "v-120-c7.m4s"},
            _h.Merger.Calls.Select(c => Path.GetFileName(c.VideoInput)).ToArray());
        Assert.IsFalse(File.Exists(_h.TargetPath("1080P 高清")));
        Assert.IsFalse(Directory.Exists(_h.WorkDirectory()), "a skipped page leaves no work files");
    }

    [TestMethod]
    public async Task Hires_and_video_fallbacks_combine()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash-hires.json");
        // Fails whatever the audio: the video is the problem.
        _h.Merger.Fail = c => Path.GetFileName(c.VideoInput) == "v-80-c7.m4s";

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        // FLAC → AAC retry, then 80 HEVC is not a fallback (only AVC at the same id); no lower id → skip.
        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.MergeFailed, outcome.Skip!.Reason);
        var calls = _h.Merger.Calls.ToList();
        Assert.AreEqual(2, calls.Count);
        Assert.IsTrue(calls[0].AllowExperimentalCodecs);
        Assert.AreEqual("a-30280.m4s", Path.GetFileName(calls[1].AudioInput));
    }

    [TestMethod]
    public async Task Finished_stream_is_reused_only_for_the_same_identity()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        var work = Directory.CreateDirectory(_h.WorkDirectory()).FullName;
        var reused = Encoding.UTF8.GetBytes("video from an earlier run");
        File.WriteAllBytes(Path.Combine(work, "v-80-c7.m4s"), reused);
        File.WriteAllBytes(Path.Combine(work, "v-80-c12.m4s"), Encoding.UTF8.GetBytes("another stream"));

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(0, _h.Cdn.Count(Video80));
        CollectionAssert.AreEqual(reused.Concat(FakeCdn.ContentOf(Audio30280)).ToArray(),
            File.ReadAllBytes(outcome.VideoPath!));
    }

    [TestMethod]
    public async Task Complete_merge_of_an_earlier_run_goes_straight_to_captions_and_the_move()
    {
        var work = Directory.CreateDirectory(_h.WorkDirectory()).FullName;
        var merged = Encoding.UTF8.GetBytes("merged earlier");
        File.WriteAllBytes(Path.Combine(work, BilibiliVideoDownloadService.MergedFileName), merged);
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(0, _h.Api.Count(FakeApi.PlayUrl));
        Assert.AreEqual(0, _h.Merger.Calls.Count);
        Assert.AreEqual(1, _h.Api.Count(FakeApi.DmView), "captions are still saved");
        CollectionAssert.AreEqual(merged, File.ReadAllBytes(outcome.VideoPath!));
    }

    #endregion

    #region durl

    [TestMethod]
    public async Task Single_mp4_segment_needs_no_merger()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-durl-mp4.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(0, _h.Merger.Calls.Count);
        CollectionAssert.AreEqual(FakeCdn.ContentOf(DurlMp4), File.ReadAllBytes(outcome.VideoPath!));
    }

    [TestMethod]
    public async Task Single_flv_segment_is_remuxed()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-durl-flv.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var call = _h.Merger.Calls.Single();
        Assert.AreEqual(StubMerger.Remux, call.Operation);
        Assert.AreEqual("seg-1-441330-1-32.flv", Path.GetFileName(call.Inputs[0]));
    }

    [TestMethod]
    public async Task Segments_are_joined_in_order()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-durl-multi.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var call = _h.Merger.Calls.Single();
        Assert.AreEqual(StubMerger.Concat, call.Operation);
        CollectionAssert.AreEqual(new[] {"seg-1-7001-1-64.flv", "seg-2-7001-2-64.flv", "seg-3-7001-3-64.flv"},
            call.Inputs.Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public async Task Failed_join_is_a_merge_skip()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-durl-multi.json");
        _h.Merger.Fail = _ => true;

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliSkipReason.MergeFailed, outcome.Skip!.Reason);
    }

    #endregion

    #region Skips and protocol failures

    [TestMethod]
    [DataRow("playurl-preview-short.json", false, BilibiliSkipReason.PreviewOnly)]
    [DataRow("playurl-preview-upower.json", false, BilibiliSkipReason.PreviewOnly)]
    [DataRow("playurl-87008.json", false, BilibiliSkipReason.SupporterOnly)]
    [DataRow("playurl-404.json", true, BilibiliSkipReason.PgcEpisodeNotSupported)]
    [DataRow("playurl-10403-region.json", false, BilibiliSkipReason.RegionRestrictedOrHidden)]
    public async Task Content_states_are_skips_without_a_cdn_request(string playUrl, bool pgc,
        BilibiliSkipReason expected)
    {
        _h.Api.On(FakeApi.PlayUrl, playUrl);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(pgc: pgc), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(expected, outcome.Skip!.Reason);
        Assert.AreEqual(0, _h.Cdn.Requests.Count);
        Assert.AreEqual(0, _h.Merger.Calls.Count);
        Assert.IsFalse(File.Exists(_h.TargetPath("1080P 高清")));
    }

    [TestMethod]
    public async Task Code_0_without_streams_is_a_protocol_failure()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-code0-nostreams.json");

        var e = await Assert.ThrowsExceptionAsync<BilibiliProtocolException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(), default));

        BilibiliSecrets.AssertClean(e);
        Assert.AreEqual(0, _h.Cdn.Requests.Count);
    }

    [TestMethod]
    public async Task Risk_control_on_playurl_is_transient()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-352.json");

        var e = await Assert.ThrowsExceptionAsync<BilibiliTemporarilyUnavailableException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(), default));

        Assert.AreEqual(BilibiliTemporaryFailureKind.RiskControl, e.Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(e, default));
        BilibiliSecrets.AssertClean(e);
    }

    #endregion

    #region CDN refresh, re-plans, dead streams

    [TestMethod]
    public async Task Dead_urls_are_refreshed_once_for_video_and_audio_together()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        // Every candidate of both streams answers 403 once.
        _h.Cdn.Fail(Video80, HttpStatusCode.Forbidden, 3).Fail(Audio30280, HttpStatusCode.Forbidden, 2);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(2, _h.Api.Count(FakeApi.PlayUrl), "the first answer + one shared refresh");
    }

    [TestMethod]
    public async Task Stream_that_is_no_longer_offered_re_plans_the_page()
    {
        // Refresh: the 80 stream is gone (the answer is the 8K one) → re-plan → 127 downloaded.
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json", "playurl-dash-8k.json");
        _h.Cdn.Fail(Video80, HttpStatusCode.Forbidden);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(3, _h.Api.Count(FakeApi.PlayUrl));
        Assert.AreEqual("v-127-c12.m4s", Path.GetFileName(_h.Merger.Calls.Single().VideoInput));
    }

    [TestMethod]
    public async Task Stream_changing_twice_is_transient()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json", "playurl-dash-8k.json", "playurl-dash-8k.json",
            "playurl-dash.json");
        _h.Cdn.Fail(Video80, HttpStatusCode.Forbidden).Fail(Video127Hevc, HttpStatusCode.Forbidden);

        var e = await Assert.ThrowsExceptionAsync<BilibiliTemporarilyUnavailableException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(), default));

        Assert.AreEqual(BilibiliTemporaryFailureKind.CdnUnavailable, e.Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(e, default));
        BilibiliSecrets.AssertClean(e);
        Assert.AreEqual(0, _h.Merger.Calls.Count);
    }

    [TestMethod]
    public async Task Durl_refresh_that_turns_into_a_preview_skips_the_page()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-durl-mp4.json", "playurl-preview-short.json");
        _h.Cdn.Fail(DurlMp4, HttpStatusCode.Forbidden);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.PreviewOnly, outcome.Skip!.Reason);
        Assert.AreEqual(0, _h.Cdn.Count("9002-1-100024.mp4"), "the preview is never downloaded");
        Assert.IsFalse(File.Exists(_h.TargetPath("1080P 高清")));
        Assert.AreEqual(0, _h.Merger.Calls.Count);
    }

    [TestMethod]
    public async Task Stream_no_cdn_host_serves_is_a_skip()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        _h.Cdn.Fail(Video80, HttpStatusCode.Forbidden);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Skipped, outcome.Status);
        Assert.AreEqual(BilibiliSkipReason.CdnUnavailable, outcome.Skip!.Reason);
        Assert.AreEqual(403, outcome.Skip.Code);
        Assert.AreEqual(3, _h.Api.Count(FakeApi.PlayUrl), "the first answer + two refreshes");
        Assert.AreEqual(0, _h.Merger.Calls.Count);
    }

    [TestMethod]
    public async Task Network_failures_on_every_url_are_transient()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        _h.Cdn.Fail(Video80, HttpStatusCode.ServiceUnavailable);

        var e = await Assert.ThrowsExceptionAsync<BilibiliTemporarilyUnavailableException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(), default));

        Assert.AreEqual(BilibiliTemporaryFailureKind.CdnUnavailable, e.Kind);
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
        Assert.IsTrue(Directory.Exists(_h.WorkDirectory()), "partials are kept for the next attempt");
    }

    #endregion

    #region Login

    [TestMethod]
    public async Task Low_quality_while_more_is_offered_checks_the_login_before_any_download()
    {
        // Page 1: myinfo fine. Page 2: only 32/16 offered with 80 listed, and the login has expired.
        _h.Api.On(FakeApi.MyInfo, "myinfo-16digit.json", "myinfo-101.json");
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json", "playurl-16-naming.json");
        await _h.Service.DownloadPageAsync(_h.Job(), default);
        var cdnBefore = _h.Cdn.Requests.Count;

        var e = await Assert.ThrowsExceptionAsync<BilibiliNotLoggedInException>(() =>
            _h.Service.DownloadPageAsync(_h.Job(5002), default));

        Assert.AreEqual(2, _h.Api.Count(FakeApi.MyInfo));
        Assert.AreEqual(cdnBefore, _h.Cdn.Requests.Count, "nothing downloaded for page 2");
        Assert.IsFalse(File.Exists(_h.TargetPath("1080P 高清", 5002)));
        Assert.IsFalse(TransientNetworkError.IsTransient(e, default));
        BilibiliSecrets.AssertClean(e);
    }

    [TestMethod]
    public async Task Low_quality_with_a_valid_login_is_downloaded()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-16-naming.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.AreEqual(2, _h.Api.Count(FakeApi.MyInfo), "the periodic check and the quality check");
        Assert.AreEqual("v-32-c7.m4s", Path.GetFileName(_h.Merger.Calls.Single().VideoInput));
    }

    [TestMethod]
    [DataRow("access")]
    [DataRow("naming")]
    [DataRow("preview")]
    [DataRow("pgc")]
    public async Task Skip_that_depends_on_the_account_confirms_the_login_first(string source)
    {
        // Page 1 checks the login (fine). The login then expires, and page 2 answers like an account without access,
        // well within the periodic check's interval: it must fail, not be recorded (and checkpointed) as a skip.
        _h.Api.On(FakeApi.MyInfo, "myinfo-16digit.json", "myinfo-101.json");
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        await _h.Service.DownloadPageAsync(_h.Job(), default);
        switch (source)
        {
            case "naming":
                _h.Api.On(FakeApi.Naming, "playurl-87008.json");
                break;
            case "preview":
                _h.Api.On(FakeApi.PlayUrl, "playurl-preview-short.json");
                break;
            case "pgc":
                _h.Api.On(FakeApi.PlayUrl, "playurl-10403-member.json");
                break;
        }

        var e = await Assert.ThrowsExceptionAsync<BilibiliNotLoggedInException>(() => _h.Service.DownloadPageAsync(
            _h.Job(5002, access: source == "access" ? new BilibiliSkip(BilibiliSkipReason.SupporterOnly) : null,
                pgc: source == "pgc"), default));

        Assert.AreEqual(2, _h.Api.Count(FakeApi.MyInfo));
        Assert.IsFalse(TransientNetworkError.IsTransient(e, default));
    }

    [TestMethod]
    public async Task Skip_for_every_account_does_not_ask_for_the_login_again()
    {
        _h.Api.On(FakeApi.MyInfo, "myinfo-16digit.json", "myinfo-101.json");
        _h.Api.On(FakeApi.PlayUrl, "playurl-10403-region.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliSkipReason.RegionRestrictedOrHidden, outcome.Skip!.Reason);
        Assert.AreEqual(1, _h.Api.Count(FakeApi.MyInfo), "only the periodic check");
    }

    [TestMethod]
    public async Task Login_is_rechecked_at_most_every_ten_minutes()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        await _h.Service.DownloadPageAsync(_h.Job(5001), default);
        await _h.Service.DownloadPageAsync(_h.Job(5002), default);
        Assert.AreEqual(1, _h.Api.Count(FakeApi.MyInfo));

        _h.Time.Advance(TimeSpan.FromMinutes(11));
        await _h.Service.DownloadPageAsync(_h.Job(5003), default);
        Assert.AreEqual(2, _h.Api.Count(FakeApi.MyInfo));
    }

    #endregion

    #region Captions, cover, hygiene

    [TestMethod]
    public async Task Captions_and_cover_are_saved_before_the_video_appears()
    {
        _h.ServeCaptions();
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        var target = _h.TargetPath("1080P 高清");
        var videoExistedWhenAsked = new ConcurrentBag<bool>();
        _h.Cdn.Route((r, _) =>
        {
            var name = FakeCdn.FileNameOf(r.RequestUri!.ToString());
            if (name.EndsWith(".json") || name.EndsWith(".xml"))
            {
                videoExistedWhenAsked.Add(File.Exists(target));
            }

            return null;
        });
        var coverAskedBeforeVideo = false;

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(getCover: _ =>
        {
            coverAskedBeforeVideo = !File.Exists(target);
            return Task.FromResult<byte[]?>([1, 2, 3]);
        }, coverUrl: "http://i0.hdslb.com/bfs/archive/example.png"), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var stem = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
        StringAssert.StartsWith(File.ReadAllText(stem + ".xml"), "<?xml");
        StringAssert.Contains(File.ReadAllText(stem + ".xml"), "<d p=");
        var primary = File.ReadAllBytes(stem + ".srt");
        CollectionAssert.AreEqual(new byte[] {0xEF, 0xBB, 0xBF}, primary.Take(3).ToArray(), "SRT has a BOM");
        StringAssert.Contains(Encoding.UTF8.GetString(primary), "00:00:00,130 --> ");
        Assert.IsTrue(File.Exists(stem + ".en-us.srt"));
        Assert.AreEqual(2, Directory.GetFiles(Path.GetDirectoryName(target)!, "*.srt").Length,
            "the AI track and the duplicate zh-Hans are not saved when a human track exists");
        CollectionAssert.AreEqual(new byte[] {1, 2, 3}, File.ReadAllBytes(stem + ".png"));
        Assert.IsTrue(coverAskedBeforeVideo);
        Assert.IsTrue(videoExistedWhenAsked.Count >= 3);
        Assert.IsFalse(videoExistedWhenAsked.Any(e => e), "every caption was fetched before the video appeared");
    }

    [TestMethod]
    public async Task Caption_failures_never_fail_the_page()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json").On(FakeApi.DmView, "dmview-352.json");
        _h.Cdn.Fail($"{Cid}.xml", HttpStatusCode.InternalServerError);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.IsTrue(File.Exists(outcome.VideoPath));
        Assert.IsTrue(_h.Logs.Lines.Any(l => l.StartsWith("Warning") && l.Contains("danmaku")));
        Assert.IsTrue(_h.Logs.Lines.Any(l => l.StartsWith("Warning") && l.Contains("subtitle list")));
    }

    [TestMethod]
    public async Task Subtitle_failure_never_fails_the_page()
    {
        _h.ServeCaptions();
        _h.Cdn.Fail("0002.json", HttpStatusCode.Forbidden);
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        var stem = Path.ChangeExtension(outcome.VideoPath!, null);
        Assert.IsFalse(File.Exists(stem + ".srt"));
        Assert.IsTrue(File.Exists(stem + ".en-us.srt"));
    }

    [TestMethod]
    public async Task Failed_sidecar_write_leaves_no_temp_file_in_the_library()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        var target = _h.TargetPath("1080P 高清");
        var cover = Path.ChangeExtension(target, ".png");
        // The cover's place is taken by a folder: writing the temp file works, moving it into place fails.
        Directory.CreateDirectory(cover);

        var outcome = await _h.Service.DownloadPageAsync(_h.Job(getCover: _ => Task.FromResult<byte[]?>([1, 2, 3]),
            coverUrl: "http://i0.hdslb.com/bfs/archive/example.png"), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        Assert.IsTrue(_h.Logs.Lines.Any(l => l.StartsWith("Warning") && l.Contains("cover")));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(target)!, "*.partial").Length);
    }

    [TestMethod]
    public async Task Cover_is_fetched_without_the_cookie()
    {
        var jpg = new byte[] {0xFF, 0xD8, 0xFF};
        _h.Cdn.File("example.jpg", Cdn.Bytes(jpg, "image/jpeg"));

        var bytes = await _h.Service.DownloadCoverAsync("http://i0.hdslb.com/bfs/archive/example.jpg", default);

        CollectionAssert.AreEqual(jpg, bytes);
        var hit = _h.Http.Hits.Single();
        Assert.AreEqual(InternalOptions.HttpClientNames.BilibiliCdn, hit.Client);
        Assert.IsFalse(hit.HasCookie);
        Assert.IsNull(await _h.Service.DownloadCoverAsync("http://i0.hdslb.com/bfs/archive/missing.jpg", default));
        Assert.IsNull(await _h.Service.DownloadCoverAsync(null, default));
    }

    [TestMethod]
    [DataRow("http://i0.hdslb.com/bfs/archive/example.png", ".png")]
    [DataRow("//i0.hdslb.com/bfs/archive/example.webp", ".webp")]
    [DataRow("http://i0.hdslb.com/bfs/archive/example", ".jpg")]
    [DataRow(null, ".jpg")]
    public void Cover_extension_follows_the_url(string? url, string expected) =>
        Assert.AreEqual(expected, BilibiliVideoDownloadService.CoverExtension(url));

    [TestMethod]
    public async Task Cdn_traffic_never_carries_the_cookie_and_logs_never_carry_signed_urls()
    {
        _h.ServeCaptions();
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        _h.Cdn.Fail(Video80, HttpStatusCode.Forbidden, 1);

        var outcome = await _h.Service.DownloadPageAsync(
            _h.Job(coverUrl: "http://i0.hdslb.com/bfs/archive/example.jpg"), default);

        Assert.AreEqual(BilibiliPageStatus.Downloaded, outcome.Status);
        foreach (var hit in _h.Http.Hits)
        {
            var isApi = new Uri(hit.Url).Host == "api.bilibili.com";
            Assert.AreEqual(isApi ? InternalOptions.HttpClientNames.Bilibili : InternalOptions.HttpClientNames.BilibiliCdn,
                hit.Client, hit.Url);
            Assert.AreEqual(isApi, hit.HasCookie, hit.Url);
        }

        Assert.IsTrue(_h.Http.Hits.Any(h => h.Url.StartsWith("https://comment.bilibili.com/")));
        Assert.IsTrue(_h.Http.Hits.Any(h => h.Url.Contains("aisubtitle.hdslb.com")));
        Assert.IsTrue(_h.Http.Hits.Any(h => h.Url.Contains("i0.hdslb.com")));
        foreach (var line in _h.Logs.Lines)
        {
            BilibiliSecrets.AssertClean(line);
            Assert.IsFalse(line.Contains("gen=playurlv3"), line);
        }
    }

    [TestMethod]
    public async Task Cancellation_keeps_the_partial_streams()
    {
        _h.Api.On(FakeApi.PlayUrl, "playurl-dash.json");
        using var cts = new CancellationTokenSource();
        _h.Cdn.File(Video80, Cdn.Serve(FakeCdn.ContentOf(Video80), stallAt: 4096, onReachedFailPoint: null));
        _h.Cdn.Route((r, _) =>
        {
            if (FakeCdn.FileNameOf(r.RequestUri!.ToString()) == Video80)
            {
                cts.CancelAfter(300);
            }

            return null;
        });

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        {
            try
            {
                await _h.Service.DownloadPageAsync(_h.Job(), cts.Token);
            }
            catch (TaskCanceledException e)
            {
                throw new OperationCanceledException(e.Message, e, e.CancellationToken);
            }
        });

        Assert.IsTrue(Directory.Exists(_h.WorkDirectory()));
        Assert.IsTrue(Directory.GetFiles(_h.WorkDirectory(), "v-80-c7.m4s.part").Length == 1);
        Assert.AreEqual(0, _h.Merger.Calls.Count);
    }

    #endregion

    #region Progress

    [TestMethod]
    public async Task Progress_gate_is_monotonic_and_single_flight_under_interleaved_reports()
    {
        var reported = new List<decimal>();
        var inside = 0;
        var maxInside = 0;
        var progress = new BilibiliPageProgress(async v =>
        {
            var now = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, now);
            lock (reported)
            {
                reported.Add(v);
            }

            await Task.Yield();
            Interlocked.Decrement(ref inside);
        }, TimeProvider.System, new CapturingLoggerFactory().CreateLogger("p"), TimeSpan.Zero);
        var video = progress.AddStream(1000);
        var audio = progress.AddStream(1000);

        var tasks = new[] {video, audio}.Select((slot, n) => Task.Run(() =>
        {
            var random = new Random(n);
            for (var i = 0; i <= 1000; i++)
            {
                // Out of order on purpose: a discarded partial file makes bytes go down.
                slot.Report(Math.Max(0, i - random.Next(0, 50)), i % 3 == 0 ? 1000 : null);
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        video.Complete(1000);
        audio.Complete(1000);
        await progress.ReportAsync(100, true);

        Assert.AreEqual(1, maxInside, "the sink is never entered twice at once");
        List<decimal> values;
        lock (reported)
        {
            values = reported.ToList();
        }

        for (var i = 1; i < values.Count; i++)
        {
            Assert.IsTrue(values[i] >= values[i - 1], $"{values[i]} after {values[i - 1]}");
        }

        Assert.AreEqual(100m, values.Last());
        Assert.IsTrue(values.All(v => v is >= 0 and <= 100));
    }

    [TestMethod]
    public async Task Merge_heartbeats_reach_the_sink_without_moving_backwards()
    {
        var reported = new List<decimal>();
        var progress = new BilibiliPageProgress(v =>
        {
            reported.Add(v);
            return Task.CompletedTask;
        }, TimeProvider.System, new CapturingLoggerFactory().CreateLogger("p"), TimeSpan.Zero);
        await progress.ReportAsync(BilibiliPageProgress.DownloadEnd, true);
        await progress.ReportMergeAsync(0.5);
        await progress.ReportMergeAsync(null);
        await progress.ReportMergeAsync(0.25);

        CollectionAssert.AreEqual(new[] {90m, 94m, 94m, 94m}, reported.ToArray());
    }

    [TestMethod]
    public async Task Reports_arriving_after_the_page_is_over_are_dropped()
    {
        var reported = new List<decimal>();
        var progress = new BilibiliPageProgress(v =>
        {
            reported.Add(v);
            return Task.CompletedTask;
        }, TimeProvider.System, new CapturingLoggerFactory().CreateLogger("p"), TimeSpan.Zero);
        var slot = progress.AddStream(100);

        await progress.CloseAsync(100);
        slot.Report(50, 100);
        await progress.ReportMergeAsync(0.5);

        CollectionAssert.AreEqual(new[] {100m}, reported.ToArray());
    }

    private void AssertProgressMonotonicEndingAt100()
    {
        var values = _h.Progress.ToList();
        for (var i = 1; i < values.Count; i++)
        {
            Assert.IsTrue(values[i] >= values[i - 1], $"{values[i]} after {values[i - 1]}");
        }

        Assert.AreEqual(100m, values.Last());
    }

    #endregion

    #region DI

    [TestMethod]
    public void Module_registers_the_cdn_client_and_the_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IMediaMerger>(new StubMerger());
        services.AddThirdParty<BilibiliOptions, BangumiOptions, DLsiteOptions, ExHentaiOptions, PixivOptions,
            SoulPlusOptions, TmdbOptions>();

        using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<BilibiliVideoDownloadService>();
        Assert.AreSame(service, provider.GetRequiredService<BilibiliVideoDownloadService>());
        Assert.IsNotNull(provider.GetRequiredService<BilibiliCdnDownloader>());
        Assert.AreEqual(ServiceLifetime.Singleton,
            services.Last(d => d.ServiceType == typeof(BilibiliVideoDownloadService)).Lifetime);
    }

    #endregion

    /// <summary>A service over fakes, with a temp folder for targets and work files.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Root = Path.Combine(Path.GetTempPath(), "bili-svc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Api.On(FakeApi.Naming, "playurl-16-naming.json")
                .On(FakeApi.MyInfo, "myinfo-16digit.json")
                .On(FakeApi.DmView, "dmview-null.json");
            var client = new BilibiliClient(new StubThirdPartyLocalizer(), Http, Logs);
            var cdn = new BilibiliCdnDownloader(Http, new BilibiliCdnDownloaderSettings
            {
                Delay = (_, _) => Task.CompletedTask,
                HeaderTimeout = TimeSpan.FromSeconds(10),
                StallTimeout = TimeSpan.FromSeconds(10),
                ProgressInterval = TimeSpan.Zero,
            }, Time, new Logger<BilibiliCdnDownloader>(Logs));
            Service = new BilibiliVideoDownloadService(client, cdn, Merger, Time,
                new Logger<BilibiliVideoDownloadService>(Logs));
        }

        public string Root { get; }
        public FakeBilibiliHttp Http { get; } = new();
        public FakeApi Api => Http.Api;
        public FakeCdn Cdn => Http.Cdn;
        public StubMerger Merger { get; } = new();
        public CapturingLoggerFactory Logs { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public BilibiliVideoDownloadService Service { get; }
        public ConcurrentQueue<string?> ResolvedNames { get; } = new();
        public ConcurrentQueue<decimal> Progress { get; } = new();

        public FavoriteItem Item(long id = Aid) => new()
        {
            Id = id, Type = 2, BvId = "BV_item", Title = "测试", Cover = "http://i0.hdslb.com/bfs/archive/item.png",
        };

        public string WorkDirectory(long cid = Cid) => Path.Combine(Root, "temp", cid.ToString());

        public string TargetPath(string? quality, long cid = Cid) =>
            Path.Combine(Root, "out", $"av{Aid}-{cid}-{quality ?? "none"}.mp4");

        public void CreateTarget(string? quality, long cid = Cid)
        {
            var path = TargetPath(quality, cid);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "downloaded earlier");
        }

        public BilibiliPageJob Job(long cid = Cid, BilibiliSkip? access = null, bool pgc = false,
            Func<CancellationToken, Task<byte[]?>>? getCover = null, string? coverUrl = null) =>
            new(Aid, cid, pgc, access, WorkDirectory(cid), (quality, _) =>
                {
                    ResolvedNames.Enqueue(quality);
                    var path = TargetPath(quality, cid);
                    return Task.FromResult(new BilibiliPageTarget(path, File.Exists(path)));
                },
                p =>
                {
                    Progress.Enqueue(p);
                    return Task.CompletedTask;
                }, getCover, true, coverUrl);

        /// <summary>dm/view with subtitles; danmaku (raw deflate), both human subtitles (gzip) and the cover.</summary>
        public void ServeCaptions()
        {
            Api.On(FakeApi.DmView, "dmview-subs.json");
            var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?><i><d p=\"1,1,25,16777215\">弹幕</d></i>");
            Cdn.File($"{Cid}.xml", Download.Cdn.Bytes(RawDeflate(xml), "text/xml", "deflate"));
            var human = Encoding.UTF8.GetBytes(BilibiliFixtures.Read("subtitle-human.json"));
            Cdn.File("0001.json", Download.Cdn.Bytes(Gzip(human), "application/json", "gzip"));
            Cdn.File("0002.json", Download.Cdn.Bytes(Gzip(human), "application/json", "gzip"));
            Cdn.File("0003.json",
                Download.Cdn.Bytes(Encoding.UTF8.GetBytes(BilibiliFixtures.Read("subtitle-ai.json")),
                    "application/json"));
            Cdn.File("example.jpg", Download.Cdn.Bytes([0xFF, 0xD8, 0xFF], "image/jpeg"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
        }

        private static byte[] RawDeflate(byte[] input)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
            {
                deflate.Write(input);
            }

            return output.ToArray();
        }

        private static byte[] Gzip(byte[] input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            {
                gzip.Write(input);
            }

            return output.ToArray();
        }
    }
}

/// <summary>A clock the test can move forward (timestamps and wall time alike).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public override long GetTimestamp() =>
        System.GetTimestamp() +
        (long) (Interlocked.Read(ref _offsetTicks) * (double) System.TimestampFrequency / TimeSpan.TicksPerSecond);
}
