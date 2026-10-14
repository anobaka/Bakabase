using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class DevicePlaybackOpenerTests
{
    [TestMethod]
    public async Task OpenerKindsComeFromTheCatalogAndInstallationIsCheckedOnThisDevice()
    {
        var store = new DevicePlaybackOverridesStore(Options.Create(new PlayerModuleOptions()));
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Vlc", "/Applications/VLC.app/Contents/MacOS/VLC");
        var snapshot = await DevicePlaybackOverrides.SnapshotAsync(store, locator, "server:other", default);

        snapshot.Openers.Single(p => p.Id == "Vlc").SupportedKinds.Should().BeEquivalentTo("audio", "video");
        snapshot.Openers.Single(p => p.Id == "Vlc").Installed.Should().BeTrue();
        snapshot.Openers.Single(p => p.Id == "Foobar2000").SupportedKinds.Should().Equal("audio");
        snapshot.Openers.Single(p => p.Id == "Foobar2000").Installed.Should().BeFalse();
        snapshot.Openers.Should().OnlyContain(p => !p.SupportedKinds!.Contains("image"));
    }
}
