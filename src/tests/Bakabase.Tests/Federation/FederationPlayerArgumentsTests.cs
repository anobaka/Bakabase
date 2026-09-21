using System;
using Bakabase.Modules.Player.Components;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationPlayerArgumentsTests
{
    private static readonly string MediaUrl = "http://localhost:34567/federation/local/media/" + new string('a', 64);

    [TestMethod]
    public void VlcTicketUsesSeekableAvioInputAndExplicitDirectProxyValue()
    {
        var arguments = FederationPlayerArguments.Build(new ResolvedPlayer("/Applications/VLC.app/Contents/MacOS/VLC", null),
            null, MediaUrl);
        Assert.AreEqual($"\"avio://{MediaUrl}\" :avio-options={{http_proxy=direct://}}", arguments);
    }

    [TestMethod]
    public void MpvScopesProxyToOneFileAndIinaPreservesItsRequiredCliArguments()
    {
        Assert.AreEqual($"--{{ --http-proxy=direct:// \"{MediaUrl}\" --}}",
            FederationPlayerArguments.Build(new ResolvedPlayer("/usr/bin/mpv", null), null, MediaUrl));
        Assert.AreEqual($"--mpv-http-proxy=direct:// --no-stdin \"{MediaUrl}\"",
            FederationPlayerArguments.Build(new ResolvedPlayer("/Applications/IINA.app/Contents/MacOS/iina-cli", "--no-stdin {0}"),
                null, MediaUrl));
    }

    [TestMethod]
    public void MappedFilesKeepTheirOrdinaryPlayerArguments()
    {
        var player = new ResolvedPlayer("/Applications/VLC.app/Contents/MacOS/VLC", "--fullscreen {0}");
        Assert.AreEqual("--fullscreen \"/Volumes/Media/a movie.mp4\"",
            FederationPlayerArguments.Build(player, "/Volumes/Media/a movie.mp4", MediaUrl));
    }

    [TestMethod]
    public void OtherKnownPlayersKeepTheirStreamArguments()
    {
        Assert.AreEqual($"\"{MediaUrl}\"",
            FederationPlayerArguments.Build(new ResolvedPlayer(@"C:\PotPlayer\PotPlayerMini64.exe", null), null, MediaUrl));
    }

    [TestMethod]
    public void ProxyBypassRejectsArbitraryUrlsAndMalformedTickets()
    {
        var player = new ResolvedPlayer("vlc", null);
        foreach (var invalid in new[]
                 {
                     MediaUrl.Replace("localhost", "example.com"),
                     MediaUrl.Replace("localhost", "localhost.example.com"),
                     MediaUrl.Replace("localhost", "user:password@localhost"),
                     MediaUrl + "?url=http://example.com", MediaUrl + "#fragment",
                     MediaUrl.Replace("/federation/local/media/", "/api/files/"),
                     MediaUrl[..^1], MediaUrl + "a", MediaUrl[..^1] + "%61",
                     MediaUrl.Replace("http:", "file:"), "--http-proxy=http://example.com"
                 })
            Assert.ThrowsException<ArgumentException>(() => FederationPlayerArguments.Build(player, null, invalid), invalid);
    }

    [TestMethod]
    public void BothLoopbackAddressFamiliesAreSupported()
    {
        foreach (var host in new[] { "127.0.0.1", "[::1]" })
        {
            var url = MediaUrl.Replace("localhost", host);
            StringAssert.Contains(FederationPlayerArguments.Build(new ResolvedPlayer("vlc", null), null, url),
                "avio://" + url);
        }
    }
}
