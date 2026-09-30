using System.Net;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiTorrentSourcesTests
{
    [DataTestMethod]
    [DataRow("10", "2", 10, 2)]
    [DataRow("0", "0", 0, 0)]
    [DataRow("1,234", "2,345", 1234, 2345)]
    [DataRow(null, null, null, null)]
    [DataRow("unknown", "2", null, 2)]
    [DataRow("-1", "-2", null, null)]
    [DataRow("2147483648", "3", null, 3)]
    [DataRow("1.5", "?", null, null)]
    [DataRow(" 7 ", " 8 ", 7, 8)]
    public async Task TorrentWindowPreservesNonnegativeSourceCountsAndKeepsUnknownCountsNullable(
        string? seeds, string? peers, int? expectedSeeds, int? expectedPeers)
    {
        var counts = (seeds == null ? "" : $"<td>Seeds: {WebUtility.HtmlEncode(seeds)}</td>") +
                     (peers == null ? "" : $"<td>Peers: {WebUtility.HtmlEncode(peers)}</td>");
        using var http = new HttpClient(new HtmlHandler($"""
            <form><table>
            <tr><td>Size: 1 MiB</td><td>Downloads: 5</td><td>Posted: 2026-09-01</td>{counts}</tr>
            <tr><td>Description</td></tr>
            <tr><td><a href='https://exhentai.org/fixture.torrent'>Download</a></td></tr>
            </table></form>
            """));
        var client = new WindowClient(new Factory(http));

        var torrent = (await client.ReadWindow()).Single();

        Assert.AreEqual(expectedSeeds, torrent.Seeds);
        Assert.AreEqual(expectedPeers, torrent.Peers);
        Assert.AreEqual(5, torrent.Downloaded, "Cumulative downloads must not be confused with active peers.");
        Assert.AreEqual(1024L * 1024, torrent.Size);
    }

    private sealed class WindowClient(IHttpClientFactory factory) : ExHentaiClient(factory, NullLoggerFactory.Instance)
    {
        public async Task<List<ExHentaiTorrent>> ReadWindow() =>
            (await GetTorrentList("https://exhentai.org/gallerytorrents.php?gid=12345&t=abcdef0123"))!;
    }

    private sealed class HtmlHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(html)});
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
