using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class RemoteAccessServiceTests
{
    private static (RemoteAccessService Service, RemoteAccessOptions Options) Build(
        RemoteAccessMode defaultMode = RemoteAccessMode.Disabled, RemoteAccessOptions? options = null)
    {
        options ??= new RemoteAccessOptions();
        var manager = new TestBOptionsManager<RemoteAccessOptions>(options);
        var service = new RemoteAccessService(manager, new RemoteAccessDefaults(defaultMode),
            NullLogger<RemoteAccessService>.Instance);
        return (service, options);
    }

    private static string Rooted(params string[] segments) =>
        Path.Combine([Path.GetPathRoot(Path.GetTempPath())!, .. segments]);

    private static async Task<(RemoteAccessService Service, RemoteDevicePairingResult Paired)> BuildPaired()
    {
        var (service, _) = Build(RemoteAccessMode.Authenticated);
        var code = await service.IssuePairingCodeAsync();
        var paired = await service.PairAsync(code.Code, "Pixel", RemoteDevicePlatform.Android);
        Assert.IsNotNull(paired);
        return (service, paired!);
    }

    #region Mode

    [TestMethod]
    public void EffectiveMode_Falls_BackToTheRuntimeDefault()
    {
        Assert.AreEqual(RemoteAccessMode.Disabled, Build().Service.GetEffectiveMode());
        Assert.AreEqual(RemoteAccessMode.Open, Build(RemoteAccessMode.Open).Service.GetEffectiveMode());
    }

    [TestMethod]
    public async Task EffectiveMode_Prefers_TheUsersChoice()
    {
        var (service, _) = Build(RemoteAccessMode.Open);
        await service.SetModeAsync(RemoteAccessMode.Disabled);

        Assert.AreEqual(RemoteAccessMode.Disabled, service.GetEffectiveMode());
    }

    [TestMethod]
    public async Task SettingAuthenticatedMode_Provisions_ASigningSecret()
    {
        var (service, options) = Build();
        await service.SetModeAsync(RemoteAccessMode.Authenticated);

        Assert.IsFalse(string.IsNullOrEmpty(options.SigningSecret));
    }

    #endregion

    #region Pairing

    [TestMethod]
    public async Task PairingCode_Is_SixDigits()
    {
        var (service, _) = Build();
        var code = await service.IssuePairingCodeAsync();

        Assert.AreEqual(6, code.Code.Length);
        Assert.IsTrue(code.Code.All(char.IsDigit));
    }

    [TestMethod]
    public async Task Pairing_Fails_WithoutACode()
    {
        var (service, _) = Build();

        Assert.IsNull(await service.PairAsync("123456", "Phone", RemoteDevicePlatform.Android));
    }

    [TestMethod]
    public async Task Pairing_Fails_WithTheWrongCode()
    {
        var (service, _) = Build();
        var issued = await service.IssuePairingCodeAsync();
        var wrong = issued.Code == "000000" ? "111111" : "000000";

        Assert.IsNull(await service.PairAsync(wrong, "Phone", RemoteDevicePlatform.Android));
    }

    [TestMethod]
    public async Task Pairing_Fails_WithAnExpiredCode()
    {
        var (service, _) = Build();
        var issued = await service.IssuePairingCodeAsync(TimeSpan.FromMilliseconds(-1));

        Assert.IsNull(await service.PairAsync(issued.Code, "Phone", RemoteDevicePlatform.Android));
        Assert.IsNull(service.GetPairingCode());
    }

    [TestMethod]
    public async Task Pairing_Consumes_TheCode()
    {
        var (service, _) = Build();
        var issued = await service.IssuePairingCodeAsync();

        Assert.IsNotNull(await service.PairAsync(issued.Code, "First", RemoteDevicePlatform.Android));
        // A code that stayed valid would let a second device pair off one handshake.
        Assert.IsNull(await service.PairAsync(issued.Code, "Second", RemoteDevicePlatform.IOS));
    }

    [TestMethod]
    public async Task Pairing_Stores_OnlyTheTokenHash()
    {
        var (service, options) = Build();
        var issued = await service.IssuePairingCodeAsync();
        var paired = await service.PairAsync(issued.Code, "Pixel", RemoteDevicePlatform.Android);

        Assert.IsNotNull(paired);
        var stored = options.Devices.Single();
        Assert.AreNotEqual(paired!.Token, stored.TokenHash);
        Assert.IsFalse(stored.TokenHash.Contains(paired.Token, StringComparison.Ordinal));
    }

    #endregion

    #region Device authentication

    [TestMethod]
    public async Task Authenticate_Accepts_TheIssuedToken()
    {
        var (service, paired) = await BuildPaired();

        var device = service.Authenticate(paired.Token);

        Assert.IsNotNull(device);
        Assert.AreEqual(paired.Device.Id, device!.Id);
    }

    [TestMethod]
    public async Task Authenticate_Rejects_AnythingElse()
    {
        var (service, paired) = await BuildPaired();

        Assert.IsNull(service.Authenticate(null));
        Assert.IsNull(service.Authenticate(""));
        Assert.IsNull(service.Authenticate("not-a-token"));
        Assert.IsNull(service.Authenticate(paired.Token + "x"));
    }

    [TestMethod]
    public async Task Revoking_ADevice_InvalidatesItsToken()
    {
        var (service, paired) = await BuildPaired();
        await service.RevokeDeviceAsync(paired.Device.Id);

        Assert.IsNull(service.Authenticate(paired.Token));
        Assert.AreEqual(0, service.GetDevices().Count);
    }

    [TestMethod]
    public async Task Devices_Get_DistinctTokens()
    {
        var (service, first) = await BuildPaired();
        var code = await service.IssuePairingCodeAsync();
        var second = await service.PairAsync(code.Code, "iPad", RemoteDevicePlatform.IOS);

        Assert.IsNotNull(second);
        Assert.AreNotEqual(first.Token, second!.Token);
        Assert.AreEqual(first.Device.Id, service.Authenticate(first.Token)!.Id);
        Assert.AreEqual(second.Device.Id, service.Authenticate(second.Token)!.Id);
    }

    #endregion

    #region Signed path tokens

    [TestMethod]
    public async Task SignedToken_Authorizes_ThePathItWasIssuedFor()
    {
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");

        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromHours(1));

        Assert.IsTrue(service.TryValidatePathToken(token, path, out var device));
        Assert.AreEqual(paired.Device.Id, device!.Id);
    }

    [TestMethod]
    public async Task SignedToken_DoesNot_AuthorizeAnotherPath()
    {
        var (service, paired) = await BuildPaired();
        var token = await service.SignPathTokenAsync(paired.Device.Id,
            Rooted("media", "library", "ep1.mkv"), TimeSpan.FromHours(1));

        Assert.IsFalse(service.TryValidatePathToken(token, Rooted("media", "library", "ep2.mkv"), out _));
    }

    [TestMethod]
    public async Task SignedToken_Accepts_TheSamePathWrittenDifferently()
    {
        // A player may echo the URL back with '..' or mixed separators in it; the
        // token is bound to the resolved path, not the spelling.
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");
        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromHours(1));

        var equivalent = Rooted("media", "library", "sub", "..", "ep1.mkv");

        Assert.IsTrue(service.TryValidatePathToken(token, equivalent, out _));
    }

    [TestMethod]
    public async Task SignedToken_Expires()
    {
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");
        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromSeconds(-10));

        Assert.IsFalse(service.TryValidatePathToken(token, path, out _));
    }

    [TestMethod]
    public async Task SignedToken_Rejects_ATamperedSignature()
    {
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");
        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromHours(1));

        var parts = token.Split('.');
        // Flip the last character of the signature.
        var signature = parts[3];
        parts[3] = signature[..^1] + (signature[^1] == 'A' ? 'B' : 'A');

        Assert.IsFalse(service.TryValidatePathToken(string.Join('.', parts), path, out _));
    }

    [TestMethod]
    public async Task SignedToken_Rejects_AnExtendedExpiry()
    {
        // The expiry is inside the signed payload, so pushing it out breaks the MAC.
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");
        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromSeconds(-10));

        var parts = token.Split('.');
        parts[2] = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds().ToString();

        Assert.IsFalse(service.TryValidatePathToken(string.Join('.', parts), path, out _));
    }

    [TestMethod]
    public async Task SignedToken_Stops_WorkingWhenItsDeviceIsRevoked()
    {
        var (service, paired) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");
        var token = await service.SignPathTokenAsync(paired.Device.Id, path, TimeSpan.FromHours(1));

        await service.RevokeDeviceAsync(paired.Device.Id);

        Assert.IsFalse(service.TryValidatePathToken(token, path, out _));
    }

    [TestMethod]
    public async Task SignedToken_Rejects_MalformedInput()
    {
        var (service, _) = await BuildPaired();
        var path = Rooted("media", "library", "ep1.mkv");

        Assert.IsFalse(service.TryValidatePathToken(null, path, out _));
        Assert.IsFalse(service.TryValidatePathToken("", path, out _));
        Assert.IsFalse(service.TryValidatePathToken("garbage", path, out _));
        Assert.IsFalse(service.TryValidatePathToken("1.a.b", path, out _));
        Assert.IsFalse(service.TryValidatePathToken("9.a.99999999999.sig", path, out _));
    }

    [TestMethod]
    public async Task SignedToken_Rejects_AnUnusablePath()
    {
        var (service, paired) = await BuildPaired();
        var token = await service.SignPathTokenAsync(paired.Device.Id,
            Rooted("media", "library", "ep1.mkv"), TimeSpan.FromHours(1));

        Assert.IsFalse(service.TryValidatePathToken(token, null, out _));
        Assert.IsFalse(service.TryValidatePathToken(token, "relative/ep1.mkv", out _));
    }

    #endregion
}
