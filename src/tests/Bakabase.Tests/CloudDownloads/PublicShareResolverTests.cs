using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PublicShareResolverTests
{
    private static readonly CloudSource Pik = new() {Id = "fixture", ProviderId = "pikpak", Url = "https://mypikpak.com/s/fixture"};
    private static readonly CloudSource SharePoint = new() {Id = "fixture", ProviderId = "oneDrive", Url = "https://tenant.sharepoint.com/:f:/g/personal/user/fixture?e=secret"};

    [TestMethod]
    public async Task Pikpak_recurses_and_paginates_then_resolves_the_exact_original_not_the_transcode()
    {
        var calls = new List<Uri>();
        var handler = new Reply(request =>
        {
            var uri = request.RequestUri!; calls.Add(uri);
            if (uri.AbsolutePath == "/v1/shield/captcha/init") return Json(new {captcha_token = "fixture-captcha"});
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (uri.AbsolutePath.EndsWith("/file_info"))
            {
                Assert.AreEqual("anonymous-share-token", query["pass_code_token"].ToString());
                return Json(new {file_info = File("inside", "inside.bin", 3,
                    original: "https://dl-z01a.mypikpak.com/fixture-original", media: "https://dl-z01a.mypikpak.com/fixture-transcode")});
            }
            if (query["parent_id"] == "folder")
                return Json(new {share_status = "OK", files = new[] {File("inside", "inside.bin", 3)}, next_page_token = "", file_count = 1});
            return query["page_token"] == "" ? Json(new
            {
                share_status = "OK", next_page_token = "next", file_count = 3, pass_code_token = "anonymous-share-token",
                files = new object[] {new {id = "folder", name = "Nested", kind = "drive#folder"}, File("first", "first.bin", 1)}
            }) : Json(new {share_status = "OK", files = new[] {File("last", "last.bin", 2)}, next_page_token = "", file_count = 3});
        });
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var result = await provider.ResolveAsync(Pik, client, default);
        CollectionAssert.AreEquivalent(new[] {"Nested/inside.bin", "first.bin", "last.bin"}, result.Files.Select(f => f.Path).ToArray());
        var original = await result.OriginalUrl(result.Files.Single(f => f.Id == "inside"), false, default);
        Assert.AreEqual("https://dl-z01a.mypikpak.com/fixture-original", original.AbsoluteUri);
        Assert.AreEqual(3, calls.Count(u => u.AbsolutePath.EndsWith("/detail")));
    }

    [TestMethod]
    public async Task Pikpak_selected_folder_limits_the_manifest_to_that_folder_and_does_not_visit_its_sibling()
    {
        const string share = "VP34KSLprDtv30ftAvk2FVfao2";
        const string selected = "VP34AFYjetF32396Hp3N0Sp3o2";
        var visited = new List<string>();
        var handler = new Reply(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/shield/captcha/init") return Json(new {captcha_token = "fixture"});
            var parent = QueryHelpers.ParseQuery(request.RequestUri.Query)["parent_id"].ToString(); visited.Add(parent);
            if (parent == "") return Json(new {share_status = "OK", next_page_token = "", file_count = 2,
                files = new[] {new {id = selected, name = "Selected", kind = "drive#folder"}, new {id = "unrelated", name = "Other", kind = "drive#folder"}}});
            Assert.AreEqual(selected, parent);
            return Json(new {share_status = "OK", next_page_token = "", files = new[] {File("original", "archive.7z", 3)}});
        });
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var result = await provider.ResolveAsync(Pik with {Url = $"https://mypikpak.com/s/{share}/AAAAKXTK1uoYBDiXHGT7xH2to2_VP3"}, client, default);
        Assert.AreEqual("archive.7z", result.Files.Single().Path);
        CollectionAssert.AreEqual(new[] {"", selected}, visited);
    }

    [TestMethod]
    public async Task Pikpak_selected_item_must_belong_to_each_requested_parent_instead_of_widening_to_all_files()
    {
        var handler = PikHandler(File("unrelated", "other.bin", 3));
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.ResolveAsync(Pik with
        {Url = "https://mypikpak.com/s/VP34KSLprDtv30ftAvk2FVfao2/AAAAKXTK1uoYBDiXHGT7xH2to2_VP3"}, client, default));
        StringAssert.Contains(error.Message, "not present in this share path");
    }

    [TestMethod]
    public async Task Pikpak_never_substitutes_a_video_derivative_for_an_unavailable_original()
    {
        var handler = PikHandler(File("one", "archive.rar", 3, media: "https://dl.mypikpak.com/transcode"));
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var result = await provider.ResolveAsync(Pik, client, default);
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => result.OriginalUrl(result.Files[0], false, default));
        StringAssert.Contains(error.Message, "PIKPAK_ORIGINAL_FILE_UNAVAILABLE");
        Assert.IsFalse(error.Message.Contains("https://"));
    }

    [TestMethod]
    public async Task Pikpak_unlocks_with_source_code_and_refreshes_expired_captcha_on_non_success_http()
    {
        var initCount = 0;
        var detailCount = 0;
        var unlocked = false;
        var handler = new Reply(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/v1/shield/captcha/init") return Json(new {captcha_token = "captcha-" + ++initCount});
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (uri.AbsolutePath == "/drive/v1/share")
            {
                Assert.AreEqual("private-code", query["pass_code"].ToString()); unlocked = true;
                return Json(new {share_status = "OK", pass_code_token = "private-pass-token"});
            }
            if (++detailCount == 1) return Json(new {error_code = 9, error = "captcha_invalid"}, HttpStatusCode.BadRequest);
            Assert.AreEqual("private-pass-token", query["pass_code_token"].ToString());
            Assert.AreEqual("captcha-2", request.Headers.GetValues("X-Captcha-Token").Single());
            return Json(new {share_status = "OK", files = new[] {File("one", "file.bin", 3)}, next_page_token = ""});
        });
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var result = await provider.ResolveAsync(Pik with {AccessCode = "private-code"}, client, default);
        Assert.IsTrue(unlocked);
        Assert.AreEqual(2, initCount);
        Assert.AreEqual(1, result.Files.Count);
    }

    [DataTestMethod]
    [DataRow("missingCursor")]
    [DataRow("repeatedPage")]
    [DataRow("duplicateEntry")]
    [DataRow("incompletePhase")]
    [DataRow("wrongCount")]
    [DataRow("unknownKind")]
    public async Task Pikpak_incomplete_listings_fail_instead_of_returning_partial_success(string scenario)
    {
        var handler = new Reply(request => request.RequestUri!.AbsolutePath == "/v1/shield/captcha/init"
            ? Json(new {captcha_token = "fixture"}) : scenario switch
            {
                "missingCursor" => Json(new {share_status = "OK", files = new[] {File("one", "file.bin", 3)}}),
                "incompletePhase" => Json(new {share_status = "OK", next_page_token = "", files = new[] {new {id = "one", name = "file", kind = "drive#file", size = "3", phase = "PHASE_TYPE_RUNNING"}}}),
                "wrongCount" => Json(new {share_status = "OK", next_page_token = "", file_count = 2, files = new[] {File("one", "file.bin", 3)}}),
                "unknownKind" => Json(new {share_status = "OK", next_page_token = "", files = new[] {new {id = "one", name = "file", kind = "shortcut"}}}),
                "duplicateEntry" => Json(new {share_status = "OK", next_page_token = "", files = new[] {File("one", "a.bin", 3), File("one", "b.bin", 3)}}),
                _ => Json(new {share_status = "OK", next_page_token = "repeat", files = new[] {File("one", "a.bin", 3)}})
            });
        using var client = new HttpClient(handler);
        var provider = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.ResolveAsync(Pik, client, default));
    }

    [TestMethod]
    public async Task Sharepoint_recurses_all_nextlinks_and_refreshes_temporary_links_without_losing_file_identity()
    {
        var pageLoads = 0;
        var handler = new Reply(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.StartsWith("/:f:")) return PublicPage(++pageLoads);
            if (uri.AbsolutePath.Contains("/root:")) return Json(new {id = "root", folder = new {childCount = 3}});
            if (uri.AbsolutePath.EndsWith("/items/inside")) return Json(SpFile("inside", "inside.bin", "refreshed-" + pageLoads));
            if (uri.AbsolutePath.EndsWith("/items/folder/children")) return Json(new {value = new[] {SpFile("inside", "inside.bin")}});
            if (QueryHelpers.ParseQuery(uri.Query).ContainsKey("$skiptoken")) return Json(new {value = new[] {SpFile("last", "last.bin")}});
            return Json(new Dictionary<string, object>
            {
                ["value"] = new object[] {new {id = "folder", name = "Nested", folder = new {childCount = 1}}, SpFile("first", "first.bin")},
                ["@odata.nextLink"] = "https://tenant.sharepoint.com/personal/user/_api/v2.0/drives/drive/items/root/children?tempauth=secret&$skiptoken=next"
            });
        });
        using var client = new HttpClient(handler);
        var provider = new SharePointCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var result = await provider.ResolveAsync(SharePoint with {AccessCode = "optional-parsed-code"}, client, default);
        Assert.AreEqual(3, result.Files.Count);
        var inside = result.Files.Single(f => f.Id == "inside");
        Assert.AreEqual("Nested/inside.bin", inside.Path);
        Assert.AreEqual("https://tenant.sharepoint.com/download/inside", (await result.OriginalUrl(inside, false, default)).AbsoluteUri);
        Assert.AreEqual("https://tenant.sharepoint.com/download/refreshed-2", (await result.OriginalUrl(inside, true, default)).AbsoluteUri);
        Assert.AreEqual(2, pageLoads);
    }

    [DataTestMethod]
    [DataRow("ready", true)]
    [DataRow("login", false)]
    [DataRow("originalMissing", false)]
    [DataRow("networkFailure", false)]
    public async Task Sharepoint_recovery_reads_complete_public_metadata_without_payloads_or_new_state(string scenario, bool available)
    {
        var root = Directory.CreateTempSubdirectory("sharepoint-recovery-").FullName;
        try
        {
            var calls = new List<Uri>();
            var handler = new Reply(request =>
            {
                Assert.AreEqual(HttpMethod.Get, request.Method);
                var uri = request.RequestUri!; calls.Add(uri);
                Assert.IsFalse(uri.AbsolutePath.StartsWith("/download/"), "Recovery must not open payload URLs.");
                if (scenario == "networkFailure") throw new HttpRequestException("TLS connection failed");
                if (uri.AbsolutePath.StartsWith("/:f:")) return scenario == "login"
                    ? new(HttpStatusCode.OK) {Content = new StringContent("Sign in")}
                    : PublicPage(1);
                if (uri.AbsolutePath.Contains("/root:")) return Json(new {id = "root", folder = new {childCount = 1}});
                var file = (Dictionary<string, object>)SpFile("one", "one.bin");
                if (scenario == "originalMissing") file.Remove("@content.downloadUrlNoAuth");
                return Json(new {value = new[] {file}});
            });
            var provider = new SharePointCloudProvider(Path.Combine(root, "app"), new NoDownload(), new Clients(handler));
            Assert.IsTrue(provider.CanRevalidateDownloadFailure("PUBLIC_SHARE_NETWORK_ERROR"));
            foreach (var code in new[] {"SHAREPOINT_PUBLIC_ACCESS_REQUIRED", "PUBLIC_SHARE_DOWNLOAD_INCOMPLETE",
                         "CLOUD_TOOL_FAILED", "SOURCE_CONFIRMED_UNAVAILABLE", "PUBLIC_SHARE_NETWORK_ERROR extra"})
                Assert.IsFalse(provider.CanRevalidateDownloadFailure(code));
            var request = new CloudDownloadRequest("same-original-operation", SharePoint, null, null, Path.Combine(root, "output"));
            var result = await provider.RevalidateDownloadAsync(request, default);
            Assert.AreEqual(available, result.Available, result.Problem);
            Assert.IsTrue(calls.Count > 0);
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(root).Any(), "A read-only preview must not create receipts, staging or files.");
        }
        finally {Directory.Delete(root, true);}
    }

    [DataTestMethod]
    [DataRow("login")]
    [DataRow("crossHostCursor")]
    [DataRow("missingValue")]
    [DataRow("wrongCount")]
    [DataRow("restrictedFile")]
    public async Task Sharepoint_rejects_login_partial_or_unsafe_listings(string scenario)
    {
        var outsideCalls = 0;
        var handler = new Reply(request =>
        {
            var uri = request.RequestUri!;
            if (uri.IdnHost != "tenant.sharepoint.com") outsideCalls++;
            if (uri.AbsolutePath.StartsWith("/:f:")) return scenario == "login" ? new(HttpStatusCode.OK) {Content = new StringContent("Sign in")} : PublicPage(1);
            if (uri.AbsolutePath.Contains("/root:")) return Json(new {id = "root", folder = new {childCount = scenario == "wrongCount" ? 2 : 1}});
            return scenario switch
            {
                "crossHostCursor" => Json(new Dictionary<string, object> {["value"] = new[] {SpFile("one", "one.bin")}, ["@odata.nextLink"] = "https://outside.invalid/collect?tempauth=secret"}),
                "missingValue" => Json(new {error = new {code = "accessDenied"}}),
                "restrictedFile" => Json(new {value = new[] {new {id = "one", name = "one.bin", size = 3}}}),
                _ => Json(new {value = new[] {SpFile("one", "one.bin")}})
            };
        });
        using var client = new HttpClient(handler);
        var provider = new SharePointCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.ResolveAsync(SharePoint, client, default));
        Assert.AreEqual(0, outsideCalls);
    }

    [TestMethod]
    public void Native_share_adapters_accept_only_their_exact_public_share_shapes()
    {
        var handler = new Reply(_ => throw new AssertFailedException("No network expected"));
        var pik = new PikPakCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        var sp = new SharePointCloudProvider(Path.GetTempPath(), new NoDownload(), new Clients(handler));
        foreach (var url in new[] {"https://mypikpak.com/drive", "https://mypikpak.com.attacker.test/s/id", "https://user:secret@mypikpak.com/s/id", "http://mypikpak.com/s/id"})
            Assert.ThrowsException<ArgumentException>(() => pik.ValidateSource(Pik with {Url = url}));
        foreach (var url in new[] {"https://onedrive.live.com/?id=one", "https://tenant.sharepoint.com/sites/private", "https://tenant.sharepoint.com.attacker.test/:f:/g/x", "https://tenant.sharepoint.com:8080/:f:/g/x"})
            Assert.ThrowsException<ArgumentException>(() => sp.ValidateSource(SharePoint with {Url = url}));
    }

    private static object File(string id, string name, int size, string? original = null, string? media = null) => new
    {id, name, size = size.ToString(), kind = "drive#file", modified_time = "2026-10-20T00:00:00Z", web_content_link = original,
        medias = new[] {new {link = new {url = media}}}};
    private static object SpFile(string id, string name, string? download = null) => new Dictionary<string, object>
    { ["id"] = id, ["name"] = name, ["size"] = 3, ["eTag"] = "version-1", ["file"] = new { },
      ["@content.downloadUrlNoAuth"] = "https://tenant.sharepoint.com/download/" + (download ?? id)};
    private static Reply PikHandler(object file) => new(request => request.RequestUri!.AbsolutePath switch
    {
        "/v1/shield/captcha/init" => Json(new {captcha_token = "fixture"}),
        "/drive/v1/share/file_info" => Json(new {share_status = "OK", file_info = file}),
        _ => Json(new {share_status = "OK", files = new[] {file}, next_page_token = ""})
    });
    private static HttpResponseMessage PublicPage(int generation)
    {
        var fields = new Dictionary<string, string>
        {
            [".driveUrl"] = "https://tenant.sharepoint.com/personal/user/_api/v2.0/drives/drive",
            [".driveAccessToken"] = "tempauth=private-token-" + generation,
            ["listUrl"] = "/personal/user/Documents"
        };
        return new(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, "https://tenant.sharepoint.com/personal/user/Documents/Forms/AllItems.aspx?id=%2Fpersonal%2Fuser%2FDocuments%2FShared"),
            Content = new StringContent("<script>const context=" + JsonSerializer.Serialize(fields) + ";</script>")
        };
    }
    internal static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")};
    internal sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
    internal sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class NoDownload : IHttpDownloader
    { public Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken cancellationToken) => throw new AssertFailedException("Resolver must not download files."); }
}
