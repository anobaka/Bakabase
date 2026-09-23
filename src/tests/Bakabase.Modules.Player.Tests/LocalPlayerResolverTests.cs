using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Tests.Helpers;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public sealed class LocalPlayerResolverTests
{
    [TestMethod]
    public void OptionalCandidateFilterDoesNotChangeOrdinaryResolution()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Vlc", "/usr/bin/vlc");
        locator.Set("Mpv", "/usr/bin/mpv");
        var resolver = new LocalPlayerResolver(locator);
        resolver.ResolveInstalled("movie.mp4")!.ExecutablePath.Should().Be("/usr/bin/vlc");
        resolver.ResolveInstalled("movie.mp4", candidate => candidate == KnownPlayerDefinitions.Mpv)!
            .ExecutablePath.Should().Be("/usr/bin/mpv");
        resolver.ResolveInstalled("movie.mp4", _ => false).Should().BeNull();
        resolver.ResolveInstalled("movie.mp4")!.ExecutablePath.Should().Be("/usr/bin/vlc");
    }

    [TestMethod]
    public void CandidateFilterDoesNotBypassSupportedExtensions()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Foobar2000", "foobar2000.exe");
        var resolver = new LocalPlayerResolver(locator);
        resolver.ResolveInstalled("movie.mp4", candidate => candidate == KnownPlayerDefinitions.Foobar2000).Should().BeNull();
        resolver.ResolveInstalled("sound.mp3", candidate => candidate == KnownPlayerDefinitions.Foobar2000)!
            .ExecutablePath.Should().Be("foobar2000.exe");
    }
}
