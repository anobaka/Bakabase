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
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class AdvertisedRemoteAddressTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Options : IBOptionsManager<RemoteAccessOptions>
    {
        public RemoteAccessOptions Value { get; private set; } = new();
        public int Saves;
        public void Save(RemoteAccessOptions value) { Value = value; Saves++; }
        public Task SaveAsync(RemoteAccessOptions value) { Save(value); return Task.CompletedTask; }
        public Task SaveAsync(Action<RemoteAccessOptions> change) { change(Value); Saves++; return Task.CompletedTask; }
    }

    private sealed class Listener : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() => ["http://0.0.0.0:8080"];
    }

    private static (RemoteAccessService Service, Options Options) Create(bool container = true,
        string? metadata = null, Clock? clock = null, Func<IReadOnlyList<RemoteAccessAddress>>? interfaces = null)
    {
        var options = new Options();
        return (new RemoteAccessService(options, new(RemoteAccessMode.Unrestricted), new("test"), new Listener(),
            NullLogger<RemoteAccessService>.Instance, addressEnvironment: new()
            {
                IsContainer = container, EndpointMetadata = metadata, Clock = clock ?? new Clock(),
                ReadInterfaceAddresses = interfaces ?? (() => throw new AssertFailedException("Container network must not be enumerated"))
            }), options);
    }

    [TestMethod]
    [DataRow("HTTP://EXAMPLE.com:80/", "http://example.com")]
    [DataRow("https://example.com:443/", "https://example.com")]
    [DataRow("http://192.168.3.23:34567/", "http://192.168.3.23:34567")]
    [DataRow("https://[2001:db8::1]:8443/", "https://[2001:db8::1]:8443")]
    [DataRow("http://nas.local:8080", "http://nas.local:8080")]
    public void OriginsAreCanonicalWithoutProbingThem(string input, string expected)
    {
        Assert.IsTrue(AdvertisedRemoteAddress.TryNormalize(input, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("nas.local:8080")]
    [DataRow("ftp://nas.local")]
    [DataRow("http://user:pass@nas.local")]
    [DataRow("http://nas.local/base")]
    [DataRow("http://nas.local/a/../")]
    [DataRow("http://nas.local/?x=1")]
    [DataRow("http://nas.local/#fragment")]
    [DataRow("http://nas.local:0")]
    [DataRow("http://nas.local\\")]
    [DataRow("http://localhost:34567")]
    [DataRow("http://pc.localhost.:34567")]
    [DataRow("http://127.0.0.1:34567")]
    [DataRow("http://127.1:34567")]
    [DataRow("http://0.0.0.0:34567")]
    [DataRow("http://169.254.1.2:34567")]
    [DataRow("http://198.18.0.1:34567")]
    [DataRow("http://198.19.255.1:34567")]
    [DataRow("http://[::]:34567")]
    [DataRow("http://[::1]:34567")]
    [DataRow("http://[::ffff:127.0.0.1]:34567")]
    [DataRow("http://[fe80::1]:34567")]
    [DataRow("http://host.docker.internal:34567")]
    [DataRow("http://224.0.0.1:34567")]
    public void InvalidAndNonShareableOriginsAreRefused(string address) =>
        Assert.IsFalse(AdvertisedRemoteAddress.TryNormalize(address, out _), address);

    [TestMethod]
    public async Task ExplicitChoicePersistsAndWinsWithoutLosingOtherBrowsersOrPorts()
    {
        var clock = new Clock();
        var (service, options) = Create(metadata: """{"schemaVersion":1,"addresses":["http://192.168.3.23:34567/","http://192.168.3.23:45678"]}""", clock: clock);
        service.ObserveAddress("http://192.168.3.23:34567");
        clock.Now += TimeSpan.FromMinutes(1);
        service.ObserveAddress("HTTPS://media.example.com:443/");
        await service.SetAdvertisedAddressAsync("https://media.example.com");
        var addresses = service.GetReachableAddresses();
        CollectionAssert.AreEqual(new[] {"https://media.example.com", "http://192.168.3.23:34567", "http://192.168.3.23:45678"}, addresses.Select(a => a.Url).ToArray());
        CollectionAssert.AreEqual(new[] {"configured", "browser", "deployment"}, addresses.Select(a => a.Source).ToArray());
        Assert.AreEqual(1, addresses.Count(a => a.Recommended));
        Assert.AreEqual("https://media.example.com", options.Value.AdvertisedAddress);
        Assert.AreEqual(1, options.Saves, "Browser observations must not write settings");
        await service.SetAdvertisedAddressAsync(null);
        Assert.IsNull(options.Value.AdvertisedAddress);
        Assert.AreEqual("browser", service.GetReachableAddresses()[0].Source);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SetAdvertisedAddressAsync("http://127.0.0.1:8080"));
        Assert.AreEqual(2, options.Saves);
    }

    [TestMethod]
    public void CandidateMemoryIsBoundedExpiresAndDoesNotBecomePersistentConfiguration()
    {
        var clock = new Clock();
        var (service, options) = Create(clock: clock);
        for (var i = 1; i <= 9; i++)
        {
            service.ObserveAddress($"http://192.168.1.{i}:34567");
            clock.Now += TimeSpan.FromMinutes(1);
        }
        var addresses = service.GetReachableAddresses();
        Assert.AreEqual(8, addresses.Count);
        Assert.AreEqual("http://192.168.1.9:34567", addresses[0].Url);
        Assert.IsFalse(addresses.Any(a => a.Url == "http://192.168.1.1:34567"));
        Assert.AreEqual(0, options.Saves);
        Assert.IsNull(options.Value.AdvertisedAddress);
        clock.Now += TimeSpan.FromHours(24);
        Assert.AreEqual(0, service.GetReachableAddresses().Count);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("not json")]
    [DataRow("{\"schemaVersion\":2,\"addresses\":[\"http://192.168.3.23:34567\"]}")]
    [DataRow("{\"schemaVersion\":1,\"addresses\":[\"http://127.0.0.1:34567\",\"http://198.18.0.2:34567\"]}")]
    public void ContainerWithoutUsableHostMetadataNeverAdvertisesItsInternalNetwork(string? metadata)
    {
        var (service, _) = Create(metadata: metadata);
        Assert.AreEqual(0, service.GetReachableAddresses().Count);
    }

    [TestMethod]
    public async Task NativeInventoryKeepsExistingInterfaceClassificationAndDeduplicatesCompleteOrigins()
    {
        var (service, _) = Create(container: false,
            metadata: """{"schemaVersion":1,"addresses":["http://192.168.3.99:9000"]}""",
            interfaces: () => [
                new("http://192.168.3.23:80/", "en0", RemoteAccessAddressKind.Lan, true),
                new("http://192.168.3.23:45678", "en0", RemoteAccessAddressKind.Lan, true),
                new("http://198.18.0.1:8080", "tun0", RemoteAccessAddressKind.Virtual),
                new("http://169.254.1.1:8080", "en1", RemoteAccessAddressKind.LinkLocal)]);
        Assert.AreEqual(4, service.GetReachableAddresses().Count);
        Assert.AreEqual(1, service.GetReachableAddresses().Count(a => a.Recommended));
        await service.SetAdvertisedAddressAsync("http://192.168.3.23/");
        var result = service.GetReachableAddresses();
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual("configured", result[0].Source);
        Assert.IsTrue(result.Skip(1).All(a => a.Source == "interface" && !a.Recommended));
        Assert.IsTrue(result.Any(a => a.Url.EndsWith(":45678")), "Host deduplication must not discard another port");
    }

    [TestMethod]
    public async Task ControllerReturns400WithoutWritingOrObservingUnsupportedAddresses()
    {
        var (service, options) = Create();
        var controller = new RemoteAccessController(service, null!, null!, null!, null!);
        var localizer = new TestBakabaseLocalizer();
        var invalid = await controller.SetAdvertisedAddress(new() {Address = "https://example.com/base"}, localizer);
        Assert.IsInstanceOfType<BadRequestObjectResult>(invalid.Result);
        Assert.AreEqual(400, ((BadRequestObjectResult) invalid.Result!).StatusCode);
        Assert.IsInstanceOfType<BadRequestObjectResult>(controller.ObserveAddress(new() {Address = "http://localhost:8080"}, localizer).Result);
        Assert.AreEqual(0, options.Saves);
        Assert.AreEqual(0, service.GetReachableAddresses().Count);
    }
}
