using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Tests.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Extensions;
using FluentAssertions;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public sealed class LocalPlayerResolverTests
{
    private static ResourceProfilePlayerOptions Profile(string executable, bool? http = null,
        params string[] extensions) => new()
    {
        Players = [new() {ExecutablePath = executable, SupportsHttp = http, Extensions = extensions.ToHashSet()}]
    };

    [TestMethod]
    public void ExistingProfileUsesLocalKnownInstallationAndPreservesCommand()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Vlc", "/Applications/VLC.app/Contents/MacOS/VLC");
        var options = Profile(@"D:\server\vlc.exe", null, " MKV ");
        options.Players![0].Command = "--fullscreen {0}";
        var result = new LocalPlayerResolver(locator).Resolve(options, @"Z:\movies\movie.MKV", requireHttp: true);
        result.ExecutablePath.Should().Be("/Applications/VLC.app/Contents/MacOS/VLC");
        result.CommandTemplate.Should().Be("--fullscreen {0}");
        result.SupportsHttp.Should().BeTrue();
    }

    [TestMethod]
    public void AutomaticLocalFallbackNeverExecutesAnUnknownRemoteProgram()
    {
        var resolver = new LocalPlayerResolver(new FakePlayerExecutableLocator());
        resolver.Resolve(Profile(@"D:\server\custom.exe"), "cover.jpg").Should().Be(ResolvedPlayer.SystemDefault);
        var explicitChoice = () => resolver.Resolve(Profile(@"D:\server\custom.exe"), "cover.jpg", explicitChoice: true);
        explicitChoice.Should().Throw<InvalidOperationException>().WithMessage("*cannot be identified*");
    }

    [TestMethod]
    public void AnExplicitMissingPlayerDoesNotFallBackToSystemDefault()
    {
        var resolver = new LocalPlayerResolver(new FakePlayerExecutableLocator());
        resolver.Resolve(Profile("vlc.exe"), "movie.mkv").Should().Be(ResolvedPlayer.SystemDefault);
        var explicitChoice = () => resolver.Resolve(Profile("vlc.exe"), "movie.mkv", explicitChoice: true);
        explicitChoice.Should().Throw<InvalidOperationException>().WithMessage("*not installed*");
        var localChoice = () => resolver.ResolveKnown("Vlc", "movie.mkv");
        localChoice.Should().Throw<InvalidOperationException>().WithMessage("*not installed*");
    }

    [TestMethod]
    public void AConfiguredAudioPlayerCannotClaimImagesOrVideo()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Foobar2000", "foobar2000.exe");
        var resolver = new LocalPlayerResolver(locator);
        var profile = Profile("foobar2000.exe", null, ".mkv", ".jpg", ".mp3");
        resolver.Resolve(profile, "video.mkv").IsSystemDefault.Should().BeTrue();
        resolver.Resolve(profile, "cover.jpg").IsSystemDefault.Should().BeTrue();
        resolver.Resolve(profile, "song.mp3").DefinitionId.Should().Be("Foobar2000");
        var explicitChoice = () => resolver.ResolveKnown("Foobar2000", "video.mkv");
        explicitChoice.Should().Throw<InvalidOperationException>().WithMessage("*does not support*");
    }

    [TestMethod]
    public void HttpOptOutOnlyRestrictsStreamsAndPersistsThroughLegacyMapping()
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Vlc", "vlc.exe");
        var resolver = new LocalPlayerResolver(locator);
        var profile = Profile("vlc.exe", false);
        resolver.Resolve(profile, "movie.mkv").ExecutablePath.Should().Be("vlc.exe");
        var streaming = () => resolver.Resolve(profile, "movie.mkv", requireHttp: true);
        streaming.Should().Throw<InvalidOperationException>().WithMessage("*not to open HTTP*");
        profile.Players![0].ToDbModel().ToDomainModel().SupportsHttp.Should().BeFalse();
        new MediaLibraryPlayer {ExecutablePath = "custom.exe"}.ToDbModel().ToDomainModel().SupportsHttp.Should().BeNull();
    }

    [TestMethod]
    [DataRow("game.exe")]
    [DataRow("archive.zip")]
    [DataRow("cover.jpg")]
    [DataRow("notes.txt")]
    [DataRow("unknown")]
    public void NonMediaIsNotMadePlayableByAnHttpTransport(string file)
    {
        var locator = new FakePlayerExecutableLocator();
        locator.Set("Vlc", "vlc.exe");
        PlayerFileCapabilities.CanStream(file).Should().BeFalse();
        new LocalPlayerResolver(locator).ResolveInstalled(file, requireHttp: true).Should().BeNull();
    }

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
