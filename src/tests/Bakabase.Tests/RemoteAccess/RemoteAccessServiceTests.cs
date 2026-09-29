using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components;
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
            new RemoteAccessHostInfo("1.2.3-test"),
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

        // At most one host is suggested, on every port, and only a LAN one.
        var recommended = addresses.Where(a => a.Recommended).ToList();
        Assert.IsTrue(recommended.Select(a => new Uri(a.Url).Host).Distinct().Count() <= 1);
        Assert.IsTrue(recommended.All(a => a.Kind == RemoteAccessAddressKind.Lan));
        Assert.IsTrue(recommended.Count is 0 or 2, string.Join(", ", recommended));

        // Offered in the classifier's order, whatever order this host lists its interfaces in:
        // the recommended host first, then the rest by what can reach them.
        var hosts = addresses.Select(a => new Uri(a.Url).Host).Distinct().ToList();
        var kindOf = hosts.Select(h => addresses.First(a => new Uri(a.Url).Host == h).Kind).ToList();
        var recommendedHost = recommended.Select(a => new Uri(a.Url).Host).FirstOrDefault();
        CollectionAssert.AreEqual(
            RemoteAccessAddressClassifier.Order(kindOf, recommendedHost == null ? null : hosts.IndexOf(recommendedHost))
                .ToArray(),
            Enumerable.Range(0, hosts.Count).ToArray(),
            string.Join(", ", addresses.Select(a => $"{a.Url} {a.Kind}")));
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

    [TestMethod]
    public async Task ServerId_IsGeneratedOnce_AndPersisted()
    {
        var (service, options) = Build();

        var first = await service.GetOrCreateServerIdAsync();
        var second = await service.GetOrCreateServerIdAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(first));
        Assert.AreEqual(first, second);
        Assert.AreEqual(first, options.ServerId);
    }

    [TestMethod]
    public async Task ServerId_KeepsAnExistingValue()
    {
        var (service, options) = Build();
        options.ServerId = "pre-existing";

        Assert.AreEqual("pre-existing", await service.GetOrCreateServerIdAsync());
    }

    [TestMethod]
    public async Task ServerId_IsReplacedOnlyWhenAsked_AndPersisted()
    {
        // A copied data directory brings its install's identity along; the copy takes a new one.
        var (service, options) = Build();
        options.ServerId = "copied-from-elsewhere";

        var replaced = await service.RegenerateServerIdAsync();

        Assert.AreNotEqual("copied-from-elsewhere", replaced);
        Assert.IsFalse(string.IsNullOrWhiteSpace(replaced));
        Assert.AreEqual(replaced, options.ServerId);
        Assert.AreEqual(replaced, await service.GetOrCreateServerIdAsync());
        Assert.AreEqual(replaced, (await service.GetServerDescriptorAsync()).Id);
    }

    [TestMethod]
    public async Task ServerDescriptor_CarriesIdentityPortAndVersions()
    {
        var (service, _) = Build(RemoteAccessMode.Disabled, "http://0.0.0.0:34567", "http://0.0.0.0:34568");

        var descriptor = await service.GetServerDescriptorAsync();

        Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.Id));
        Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.Name));
        // The first listening port is the one clients are pointed at.
        Assert.AreEqual(34567, descriptor.Port);
        Assert.AreEqual("1.2.3-test", descriptor.AppVersion);
        Assert.AreEqual(RemoteAccessProtocol.CurrentVersion, descriptor.ProtocolVersion);
    }

    [TestMethod]
    public async Task ServerDescriptor_HasNoPort_BeforeKestrelReportsOne()
    {
        var (service, _) = Build();

        Assert.IsNull((await service.GetServerDescriptorAsync()).Port);
    }

    [TestMethod]
    public async Task AllowLiveTranscode_DefaultsOff_AndPersists()
    {
        var (service, options) = Build();

        Assert.IsFalse(service.GetAllowLiveTranscode());

        await service.SetAllowLiveTranscodeAsync(true);

        Assert.IsTrue(service.GetAllowLiveTranscode());
        Assert.IsTrue(options.AllowLiveTranscode);
    }
}
