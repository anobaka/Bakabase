using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class RemoteAccessServiceTests
{
    private sealed class StubListeningAddressProvider(params string[] addresses) : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() => addresses;
    }

    private static (RemoteAccessService Service, RemoteAccessOptions Options) Build(
        RemoteAccessMode defaultMode = RemoteAccessMode.Disabled,
        params string[] listeningAddresses)
    {
        var options = new RemoteAccessOptions();
        var service = new RemoteAccessService(
            new TestBOptionsManager<RemoteAccessOptions>(options),
            new RemoteAccessDefaults(defaultMode),
            new StubListeningAddressProvider(listeningAddresses),
            NullLogger<RemoteAccessService>.Instance);

        return (service, options);
    }

    [TestMethod]
    public void EffectiveMode_Falls_BackToTheRuntimeDefault()
    {
        // A desktop install starts closed; Docker keeps serving whoever can reach it,
        // which is what containerized installs have always done.
        Assert.AreEqual(RemoteAccessMode.Disabled, Build().Service.GetEffectiveMode());
        Assert.AreEqual(RemoteAccessMode.Unrestricted,
            Build(RemoteAccessMode.Unrestricted).Service.GetEffectiveMode());
    }

    [TestMethod]
    public async Task EffectiveMode_Prefers_TheUsersChoice()
    {
        var (service, _) = Build(RemoteAccessMode.Unrestricted);
        await service.SetModeAsync(RemoteAccessMode.Disabled);

        Assert.AreEqual(RemoteAccessMode.Disabled, service.GetEffectiveMode());
    }

    [TestMethod]
    public async Task SettingModeToNull_ReturnsToTheRuntimeDefault()
    {
        var (service, options) = Build(RemoteAccessMode.Unrestricted);

        await service.SetModeAsync(RemoteAccessMode.Enabled);
        Assert.AreEqual(RemoteAccessMode.Enabled, service.GetEffectiveMode());

        await service.SetModeAsync(null);
        Assert.IsNull(options.Mode);
        Assert.AreEqual(RemoteAccessMode.Unrestricted, service.GetEffectiveMode());
    }

    [TestMethod]
    public async Task Mode_IsPersisted()
    {
        var (service, options) = Build();
        await service.SetModeAsync(RemoteAccessMode.Enabled);

        Assert.AreEqual(RemoteAccessMode.Enabled, options.Mode);
    }

    [TestMethod]
    public void ReachableAddresses_AreEmpty_WhenNothingIsListening()
    {
        var (service, _) = Build();

        Assert.AreEqual(0, service.GetReachableAddresses().Count);
    }

    [TestMethod]
    public void ReachableAddresses_UseTheListeningPorts_AndNeverLoopback()
    {
        var (service, _) = Build(RemoteAccessMode.Disabled, "http://0.0.0.0:34567", "http://0.0.0.0:34568");

        var addresses = service.GetReachableAddresses();

        // The host may legitimately have no non-loopback interface (a CI container),
        // so the assertion is about the shape of whatever comes back.
        foreach (var address in addresses)
        {
            Assert.IsTrue(address.Url.StartsWith("http://"), address.Url);
            Assert.IsFalse(address.Url.Contains("127.0.0.1"), $"loopback leaked into {address.Url}");
            Assert.IsFalse(address.Url.Contains("0.0.0.0"), $"bind wildcard leaked into {address.Url}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(address.InterfaceName));
        }

        if (addresses.Count > 0)
        {
            var ports = addresses.Select(a => new Uri(a.Url).Port).Distinct().OrderBy(p => p).ToArray();

            CollectionAssert.AreEqual(new[] {34567, 34568}, ports);
        }
    }

    [TestMethod]
    public void ReachableAddresses_Ignore_UnparseableListeningAddresses()
    {
        var (service, _) = Build(RemoteAccessMode.Disabled, "not-a-url", "http://0.0.0.0:34567");

        foreach (var address in service.GetReachableAddresses())
        {
            Assert.AreEqual(34567, new Uri(address.Url).Port);
        }
    }
}
