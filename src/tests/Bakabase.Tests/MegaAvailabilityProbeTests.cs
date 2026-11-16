using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability.Probes;

namespace Bakabase.Tests;

[TestClass]
public sealed class MegaAvailabilityProbeTests
{
    const string Folder = "https://mega.nz/folder/abc_DEF1";
    const string FileUrl = "https://mega.nz/file/abc_DEF1";
    static readonly string FolderKey = new('K', 22);
    static readonly string FileKey = new('K', 43);
    const string FolderMetadata = """[{"ph":"abc_DEF1","attrs":"encrypted","k":"sender123:encrypted","s":[0,0,0,0,0]}]""";
    const string FileMetadata = """[{"s":0,"at":"encrypted"}]""";

    [TestMethod]
    [DataRow("file", FileMetadata, "available")]
    [DataRow("legacyFile", FileMetadata, "available")]
    [DataRow("fileSeparate", FileMetadata, "available")]
    [DataRow("missingKey", FileMetadata, "accessRequired")]
    [DataRow("folder", FolderMetadata, "available")]
    [DataRow("legacyFolder", FolderMetadata, "available")]
    [DataRow("folderSeparate", FolderMetadata, "available")]
    [DataRow("child", FolderMetadata, "unknown")]
    [DataRow("child", "[-9]", "confirmedUnavailable")]
    [DataRow("file", "[-9]", "confirmedUnavailable")]
    [DataRow("folder", "[-9]", "confirmedUnavailable")]
    [DataRow("file", "[-14]", "accessRequired")]
    [DataRow("file", "[-11]", "accessRequired")]
    [DataRow("folder", "[-16]", "accessRequired")]
    [DataRow("folder", "[-18]", "temporarilyUnavailable")]
    [DataRow("file", "[-17]", "temporarilyUnavailable")]
    [DataRow("folder", "-9", "unknown")]
    [DataRow("file", "[{\"e\":-9}]", "unknown")]
    [DataRow("file", "[{\"d\":1}]", "accessRequired")]
    [DataRow("folder", "[{}]", "unknown")]
    [DataRow("folder", "[{\"ph\":\"notroot1\",\"k\":\"sender123:enc\",\"attrs\":\"x\",\"s\":[0,0,0,0,0]}]", "unknown")]
    [DataRow("file", "[{\"s\":\"0\",\"at\":42}]", "unknown")]
    public async Task Uses_only_the_public_root_handle_and_metadata_command(string kind, string body, string status)
    {
        var file = kind.Contains("file", StringComparison.OrdinalIgnoreCase) || kind == "missingKey";
        var url = kind switch
        {
            "file" => FileUrl + "#" + FileKey, "fileSeparate" or "missingKey" => FileUrl,
            "legacyFile" => "https://mega.co.nz/#!abc_DEF1!" + FileKey,
            "legacyFolder" => "https://mega.co.nz/#F!abc_DEF1!" + FolderKey,
            "folderSeparate" => Folder, "child" => Folder + "#" + FolderKey + "/file/child123",
            _ => Folder + "#" + FolderKey
        };
        var factory = new Clients(async request =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://g.api.mega.co.nz/cs?id=0", request.RequestUri!.AbsoluteUri);
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var command = data.RootElement.EnumerateArray().Single();
            Assert.AreEqual(2, command.EnumerateObject().Count());
            Assert.AreEqual(file ? "g" : "pli", command.GetProperty("a").GetString());
            Assert.AreEqual("abc_DEF1", command.GetProperty(file ? "p" : "ph").GetString());
            return new(HttpStatusCode.OK) {Content = new StringContent(body)};
        });
        var result = await new MegaAvailabilityProbe(factory).CheckAsync(new() {Id = "fixture", Url = url,
            AccessCode = kind.EndsWith("Separate") ? (file ? FileKey : FolderKey) : null}, default);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(1, factory.Calls);
        Assert.IsFalse(JsonSerializer.Serialize(result.Evidence).Contains("abc_DEF1"));
    }

    [TestMethod]
    public async Task Http_status_and_oversized_metadata_cannot_confirm_deletion()
    {
        foreach (var status in new[] {HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.OK})
        {
            var factory = new Clients(_ => Task.FromResult(new HttpResponseMessage(status)
                {Content = status == HttpStatusCode.OK ? new StreamContent(new NonSeekable(
                    Encoding.UTF8.GetBytes("[-9]" + new string(' ', 256 * 1024)))) : new StringContent("[-9]")}));
            var result = await new MegaAvailabilityProbe(factory).CheckAsync(new() {Id = "fixture", Url = FileUrl + "#" + FileKey}, default);
            Assert.AreNotEqual("confirmedUnavailable", result.Status);
            if (status == HttpStatusCode.OK) Assert.AreEqual("unknown", result.Status);
        }
    }

    [TestMethod]
    public async Task Noncanonical_locations_and_unsupported_selectors_make_no_request()
    {
        var factory = new Clients(_ => throw new AssertFailedException("Unexpected request"));
        foreach (var url in new[] {"https://mega.nz.example/file/abc_DEF1", "https://user@mega.nz/file/abc_DEF1",
            "http://mega.nz/file/abc_DEF1", Folder + "#" + FolderKey + "/arbitrary/child123"})
            Assert.AreEqual("unsupported", (await new MegaAvailabilityProbe(factory).CheckAsync(new() {Id = "fixture", Url = url}, default)).Status);
        Assert.AreEqual(0, factory.Calls);
    }

    private sealed class Clients(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public int Calls {get; private set;}
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual("Mega.PublicShareMetadata", name);
            return new(new Handler(request => {Calls++; return send(request);}));
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private sealed class NonSeekable(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
