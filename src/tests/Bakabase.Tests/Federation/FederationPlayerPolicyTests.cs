using System;
using System.Collections.Generic;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Components;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationPlayerPolicyTests
{
    [TestMethod]
    [DataRow(VlcLoopbackHttpSupport.SystemProxy)]
    [DataRow(VlcLoopbackHttpSupport.Unknown)]
    public void ProxyOrUnknownEnvironmentSkipsVlcBeforeDiscoveryAndChoosesDirectCapablePlayer(VlcLoopbackHttpSupport state)
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc, KnownPlayerDefinitions.Mpv]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(state));
        var player = policy.Resolve("film.mp4", null);
        Assert.AreEqual("Mpv", player.ExecutablePath);
        CollectionAssert.DoesNotContain(locator.Probed, "Vlc");
    }

    [TestMethod]
    public void IinaFallbackRetainsItsRequiredCliArguments()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc, KnownPlayerDefinitions.Iina]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(VlcLoopbackHttpSupport.SystemProxy));
        var player = policy.Resolve("film.mp4", null);
        Assert.AreEqual("Iina", player.ExecutablePath);
        Assert.AreEqual("--no-stdin {0}", player.CommandTemplate);
    }

    [TestMethod]
    [DataRow(VlcLoopbackHttpSupport.SystemProxy)]
    [DataRow(VlcLoopbackHttpSupport.Unknown)]
    public void UnsafeVlcOnlyInstallationReturnsActionableErrorWithoutTryingIt(VlcLoopbackHttpSupport state)
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(state));
        var error = Assert.ThrowsException<FederationQueryException>(() => policy.Resolve("film.mp4", null));
        Assert.AreEqual("PlayerProxyUnsupported", error.Code);
        Assert.AreEqual(409, error.StatusCode);
        StringAssert.Contains(error.Message, "preview");
        StringAssert.Contains(error.Message, "path mapping");
        CollectionAssert.DoesNotContain(locator.Probed, "Vlc");
    }

    [TestMethod]
    public void MappedFilesDoNotReadProxyConfigurationOrChangePlayerSelection()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc, KnownPlayerDefinitions.Iina]};
        var environment = new Environment(VlcLoopbackHttpSupport.Unknown);
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), environment);
        Assert.AreEqual("Vlc", policy.Resolve("film.mp4", "/Volumes/Media/film.mp4").ExecutablePath);
        Assert.AreEqual(0, environment.Reads);
    }

    [TestMethod]
    public void AvailableNativeHttpPreservesOrdinaryResolverPriority()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc, KnownPlayerDefinitions.Mpv]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(VlcLoopbackHttpSupport.Available));
        Assert.AreEqual("Vlc", policy.Resolve("film.mp4", null).ExecutablePath);
    }

    [TestMethod]
    public void NoProxyAndNoPlayerKeepsExistingUnavailableError()
    {
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(new Locator()),
            new Environment(VlcLoopbackHttpSupport.Available));
        var error = Assert.ThrowsException<FederationQueryException>(() => policy.Resolve("film.mp4", null));
        Assert.AreEqual("PlayerUnavailable", error.Code);
    }

    [TestMethod]
    public void AnExplicitMappedPlayerWinsOverCatalogPriorityWithoutReadingProxySettings()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.PotPlayer, KnownPlayerDefinitions.Vlc]};
        var environment = new Environment(VlcLoopbackHttpSupport.SystemProxy);
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), environment);
        Assert.AreEqual("Vlc", policy.Resolve("film.mp4", "/media/film.mp4", "Vlc").DefinitionId);
        Assert.AreEqual(0, environment.Reads);
    }

    [TestMethod]
    [DataRow("PotPlayer", VlcLoopbackHttpSupport.Available)]
    [DataRow("Vlc", VlcLoopbackHttpSupport.Unknown)]
    [DataRow("Vlc", VlcLoopbackHttpSupport.SystemProxy)]
    public void AnExplicitHttpPlayerCannotBypassTicketProxyRestrictions(string id, VlcLoopbackHttpSupport state)
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.PotPlayer, KnownPlayerDefinitions.Vlc, KnownPlayerDefinitions.Mpv]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(state));
        var error = Assert.ThrowsExactly<FederationQueryException>(() => policy.Resolve("film.mp4", null, id));
        Assert.AreEqual("PlayerProxyUnsupported", error.Code);
    }

    [TestMethod]
    public void AutomaticHttpSelectionNeverUsesAnUnverifiedPlayerEvenWhenVlcIsAllowed()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.PotPlayer, KnownPlayerDefinitions.Vlc]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(VlcLoopbackHttpSupport.Available));
        Assert.AreEqual("Vlc", policy.Resolve("film.mp4", null).DefinitionId);
        CollectionAssert.DoesNotContain(locator.Probed, "PotPlayer");
        Assert.AreEqual("PotPlayer", policy.Resolve("film.mp4", "/media/film.mp4").DefinitionId);
    }

    [TestMethod]
    public void ExplicitSystemDefaultRequiresALocalPathAndExplicitTypeMismatchesDoNotFallBack()
    {
        var locator = new Locator {Installed = [KnownPlayerDefinitions.Vlc]};
        var policy = new FederationPlayerPolicy(new LocalPlayerResolver(locator), new Environment(VlcLoopbackHttpSupport.Available));
        Assert.IsTrue(policy.Resolve("film.mp4", "/media/film.mp4", "system-default").IsSystemDefault);
        Assert.AreEqual("MappedPathUnavailable", Assert.ThrowsExactly<FederationQueryException>(() =>
            policy.Resolve("film.mp4", null, "system-default")).Code);
        Assert.AreEqual("PlayerUnavailable", Assert.ThrowsExactly<FederationQueryException>(() =>
            policy.Resolve("cover.jpg", "/media/cover.jpg", "Vlc")).Code);
        Assert.AreEqual("PlayerUnavailable", Assert.ThrowsExactly<FederationQueryException>(() =>
            policy.Resolve("film.mp4", "/media/film.mp4", "Mpv")).Code);
    }

    private sealed class Environment(VlcLoopbackHttpSupport state) : IFederationPlayerProxyEnvironment
    {
        public int Reads { get; private set; }
        public VlcLoopbackHttpSupport GetVlcLoopbackHttpSupport() { Reads++; return state; }
    }

    private sealed class Locator : IPlayerExecutableLocator
    {
        public KnownPlayerDefinition[] Installed { get; init; } = [];
        public List<string> Probed { get; } = [];
        public IReadOnlyList<string> Locate(KnownPlayerDefinition definition)
        {
            Probed.Add(definition.Id);
            return Array.IndexOf(Installed, definition) >= 0 ? [definition.Id] : [];
        }
    }
}
