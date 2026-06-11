using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class PlayerDiscoveryServiceTests
{
    [TestMethod]
    public async Task ReturnsOnlyPlayersTheLocatorFinds()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("PotPlayer", @"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe");
        locator.Set("Vlc", @"C:\Program Files\VideoLAN\VLC\vlc.exe");
        var service = new PlayerDiscoveryService(locator, NullLogger<PlayerDiscoveryService>.Instance);

        var players = await service.GetDiscoveredPlayersAsync();

        players.Select(p => p.DefinitionId).Should().BeEquivalentTo(["PotPlayer", "Vlc"]);
        players.Single(p => p.DefinitionId == "PotPlayer").Capabilities
            .Should().Be(BatchPlayCapability.PlaylistFile);
    }

    [TestMethod]
    public async Task NothingInstalled_ReturnsEmpty()
    {
        var service = new PlayerDiscoveryService(new FakePlayerExecutableLocator(),
            NullLogger<PlayerDiscoveryService>.Instance);

        (await service.GetDiscoveredPlayersAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task CachesScanResult_UntilRescanRequested()
    {
        var locator = new FakePlayerExecutableLocator();
        var service = new PlayerDiscoveryService(locator, NullLogger<PlayerDiscoveryService>.Instance);

        await service.GetDiscoveredPlayersAsync();
        var callsAfterFirstScan = locator.LocateCallCount;
        await service.GetDiscoveredPlayersAsync();
        locator.LocateCallCount.Should().Be(callsAfterFirstScan, "second call must hit the cache");

        // A player installed after the first scan shows up after a rescan.
        locator.Set("Vlc", @"C:\Program Files\VideoLAN\VLC\vlc.exe");
        (await service.GetDiscoveredPlayersAsync()).Should().BeEmpty();
        (await service.GetDiscoveredPlayersAsync(rescan: true)).Should().ContainSingle();
    }
}
