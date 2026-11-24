using System.Text.Json;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakCliCleanupAccountTests
{
    private string _root = null!;
    private Native _native = null!;
    private PikPakCliAccountService _accounts = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pikpak-cleanup-account-" + Guid.NewGuid().ToString("N"));
        _native = new();
        _accounts = new(_root, _native);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task SameAccountUpgradeKeepsOperationGenerationAndRequiresSeparateOptIn()
    {
        var original = await AuthorizeAsync();
        Assert.IsFalse(original.CanManageFiles);
        Assert.IsFalse(original.TrashTemporaryCopiesAfterDownload);
        await RejectAsync("PIKPAK_MANAGE_FILES_SCOPE_REQUIRED", () => SetCleanupAsync(original, true));

        var upgrade = await _accounts.StartManageFilesAuthorizationAsync(default);
        Assert.AreEqual("account,files:manage", _native.RequestedScope(upgrade.Id));
        Assert.AreEqual("account,files:write", _native.RequestedScope(original.AccountId!));
        Assert.AreEqual(original, await _accounts.GetAsync(default));

        _native.Approve(upgrade.Id, "same-provider-user", "account", "files:manage");
        Assert.AreEqual("authorized", (await _accounts.PollAsync(upgrade.Id, default)).Status);
        var upgraded = await _accounts.GetAsync(default);
        Assert.AreEqual(original.AccountId, upgraded.AccountId);
        Assert.AreEqual(original.AccountVersion, upgraded.AccountVersion);
        Assert.IsTrue(upgraded.CanManageFiles);
        Assert.IsFalse(upgraded.TrashTemporaryCopiesAfterDownload);
        Assert.IsFalse(JsonSerializer.Serialize(upgraded).Contains("same-provider-user"));

        Assert.IsTrue((await SetCleanupAsync(upgraded, true)).TrashTemporaryCopiesAfterDownload);
        _accounts = new(_root, _native);
        await _accounts.WithAccountAsync(original.AccountId, original.AccountVersion, async lease =>
        {
            Assert.AreEqual(upgrade.Id, Path.GetFileName(lease.Profile));
            Assert.IsTrue(lease.CanManageFiles);
            Assert.IsTrue(lease.TrashTemporaryCopiesAfterDownload);
            await lease.RequireOnline(default);
            return true;
        }, default);
        Assert.IsFalse((await SetCleanupAsync(upgraded, false)).TrashTemporaryCopiesAfterDownload);
    }

    [TestMethod]
    public async Task LegacyRegistrationRecoversIdentityFromItsOriginalCredentialProfile()
    {
        var original = await AuthorizeAsync(providerId: null);
        _native.Approve(original.AccountId!, "same-provider-user", "account", "files:write");
        _native.Calls.Clear();
        var upgrade = await _accounts.StartManageFilesAuthorizationAsync(default);
        Assert.AreEqual((original.AccountId!, "auth status"), _native.Calls[0]);
        _native.Approve(upgrade.Id, "same-provider-user", "account", "files:manage");
        Assert.AreEqual("authorized", (await _accounts.PollAsync(upgrade.Id, default)).Status);
        Assert.AreEqual(original.AccountVersion, (await _accounts.GetAsync(default)).AccountVersion);
    }

    [TestMethod]
    public async Task MissingLegacyIdentityDoesNotInitiateAnUnboundUpgrade()
    {
        var original = await AuthorizeAsync(providerId: null);
        _native.Calls.Clear();
        await RejectAsync("PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED", () => _accounts.StartManageFilesAuthorizationAsync(default));
        CollectionAssert.AreEqual(new[] {(original.AccountId!, "auth status")}, _native.Calls);
        Assert.AreEqual(original, await _accounts.GetAsync(default));
    }

    [DataTestMethod]
    [DataRow("different-provider-user", "files:manage", "PIKPAK_ACCOUNT_CHANGED")]
    [DataRow(null, "files:manage", "PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED")]
    [DataRow("same-provider-user", "files:write", "PIKPAK_MANAGE_FILES_SCOPE_REQUIRED")]
    [DataRow("same-provider-user", "files:purge", "PIKPAK_MANAGE_FILES_SCOPE_REQUIRED")]
    public async Task WrongIdentityOrGrantLeavesTheOriginalAccountUntouched(string? identity, string fileScope, string error)
    {
        var original = await AuthorizeAsync();
        var upgrade = await _accounts.StartManageFilesAuthorizationAsync(default);
        _native.Approve(upgrade.Id, identity, "account", fileScope);
        var rejected = await _accounts.PollAsync(upgrade.Id, default);
        Assert.AreEqual("failed", rejected.Status);
        Assert.AreEqual(error, rejected.Problem);
        Assert.AreEqual(original, await _accounts.GetAsync(default));
        var calls = _native.Calls.Count;
        await _accounts.PollAsync(upgrade.Id, default);
        Assert.AreEqual(calls, _native.Calls.Count);
    }

    [TestMethod]
    public async Task InterruptedIdentityCheckReusesTheRecordedGrantWithoutRepeatingConsent()
    {
        var original = await AuthorizeAsync();
        var upgrade = await _accounts.StartManageFilesAuthorizationAsync(default);
        _native.Approve(upgrade.Id, "same-provider-user", "account", "files:manage");
        _native.FailStatusOnce.Add(upgrade.Id);
        var pending = await _accounts.PollAsync(upgrade.Id, default);
        Assert.AreEqual("pending", pending.Status);
        Assert.AreEqual("PIKPAK_NETWORK_UNAVAILABLE", pending.Problem);
        Assert.AreEqual(original, await _accounts.GetAsync(default));

        var sessionPath = Path.Combine(_root, "sessions", upgrade.Id + ".json");
        var saved = await PikPakCliStorage.ReadAsync<PikPakCliAccountService.Session>(sessionPath, default);
        Assert.IsNotNull(saved);
        Assert.IsTrue(saved.ManageFilesConsentVerified);
        saved.NextPollAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await PikPakCliStorage.WriteAsync(sessionPath, saved, default);
        _accounts = new(_root, _native);
        _native.Calls.Clear();

        Assert.AreEqual("authorized", (await _accounts.PollAsync(upgrade.Id, default)).Status);
        CollectionAssert.AreEqual(new[] {(upgrade.Id, "auth status")}, _native.Calls);
        Assert.AreEqual(original.AccountVersion, (await _accounts.GetAsync(default)).AccountVersion);
    }

    [TestMethod]
    public async Task CleanupSettingsRejectStaleAccountsAndChangesDuringAnOperationLease()
    {
        var current = await AuthorizeAsync(manageFiles: true);
        Assert.IsTrue(current.CanManageFiles);
        Assert.IsFalse(current.TrashTemporaryCopiesAfterDownload);
        await RejectAsync("PIKPAK_ACCOUNT_CHANGED", () => SetCleanupAsync(current with {AccountVersion = "stale"}, true));
        await _accounts.WithAccountAsync(current.AccountId, current.AccountVersion, async lease =>
        {
            await RejectAsync("PIKPAK_ACCOUNT_IN_USE", () => SetCleanupAsync(current, true));
            Assert.IsFalse(lease.TrashTemporaryCopiesAfterDownload);
            return true;
        }, default);
        Assert.IsFalse((await _accounts.GetAsync(default)).TrashTemporaryCopiesAfterDownload);
    }

    [TestMethod]
    public async Task ManageFilesLeaseRejectsAChangedRemoteIdentityEvenWhenTheLocalVersionMatches()
    {
        var current = await AuthorizeAsync(manageFiles: true);
        _native.Approve(current.AccountId!, "wrong-live-user", "account", "files:manage");
        await _accounts.WithAccountAsync(current.AccountId, current.AccountVersion, async lease =>
        {
            await RejectAsync("PIKPAK_ACCOUNT_CHANGED", () => lease.RequireOnline(default));
            return true;
        }, default);
    }

    [TestMethod]
    public async Task LiveCapacityUsesFreshRoundedBoundsAndDoesNotSubtractTrashFromUsedSpace()
    {
        var account = await AuthorizeAsync();
        _native.SetStorage("""{"used":"1.00 GB","total":"6.00 GB","in_trash":"2 B","remainingTransferBytes":7}""");
        var since = DateTimeOffset.UtcNow;

        var first = await CapacityAsync(account);

        Assert.AreEqual("fresh", first.Status);
        Assert.AreEqual(account.AccountId, first.AccountId);
        Assert.AreEqual(account.AccountVersion, first.AccountVersion);
        Assert.IsTrue(first.CheckedAt >= since && first.CheckedAt <= DateTimeOffset.UtcNow);
        const decimal gibibyte = 1_073_741_824m;
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(0.995m * gibibyte, 1.005m * gibibyte), first.UsedBytes);
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(5.995m * gibibyte, 6.005m * gibibyte), first.TotalBytes);
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(2, 2), first.TrashBytes);
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(4.99m * gibibyte, 5.01m * gibibyte), first.AvailableBytes);

        var ordinary = await _accounts.GetAsync(default);
        Assert.IsNull(ordinary.Quota);
        Assert.AreEqual(1, _native.CapacityRequests);
        _native.SetStorage("""{"used":"0 B","total":"5 B"}""");
        var next = await CapacityAsync(account);
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(5, 5), next.AvailableBytes);
        Assert.AreEqual(2, _native.CapacityRequests);
        Assert.IsNull(next.TrashBytes);
    }

    [TestMethod]
    public async Task BusyCapacityDoesNotJoinTheProfileQueueOrReturnOldValues()
    {
        var account = await AuthorizeAsync();
        _native.SetStorage("""{"used":"1 B","total":"5 B"}""");
        Assert.AreEqual("fresh", (await CapacityAsync(account)).Status);
        _native.ProfileBusy = true;
        _native.Calls.Clear();

        var busy = await CapacityAsync(account).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual("busy", busy.Status);
        Assert.AreEqual("PIKPAK_ACCOUNT_IN_USE", busy.Problem);
        Assert.AreEqual(account.AccountId, busy.AccountId);
        Assert.AreEqual(account.AccountVersion, busy.AccountVersion);
        Assert.IsNull(busy.CheckedAt);
        Assert.IsNull(busy.UsedBytes);
        Assert.IsNull(busy.AvailableBytes);
        Assert.AreEqual(0, _native.Calls.Count, "The busy probe must never fall back to the waiting command path.");
    }

    [TestMethod]
    public async Task LiveCapacityTreatsMissingInvalidAndUnlimitedValuesSeparately()
    {
        var account = await AuthorizeAsync();
        _native.SetStorage("""{"used":"?","total":"0 B","in_trash":"unknown"}""");
        var missing = await CapacityAsync(account);
        Assert.AreEqual("unavailable", missing.Status);
        Assert.AreEqual("PIKPAK_CAPACITY_UNAVAILABLE", missing.Problem);
        Assert.IsNotNull(missing.CheckedAt);
        Assert.IsNull(missing.TotalBytes);
        Assert.IsNull(missing.AvailableBytes);

        _native.SetStorage("""{"used":"7 B","total":"0 B","is_unlimited":true}""");
        var unlimited = await CapacityAsync(account);
        Assert.AreEqual("fresh", unlimited.Status);
        Assert.IsTrue(unlimited.Unlimited);
        Assert.AreEqual(new PikPakCliCapacityRangeViewModel(7, 7), unlimited.UsedBytes);
        Assert.IsNull(unlimited.AvailableBytes);
        Assert.IsNull(unlimited.TotalBytes);
    }

    [TestMethod]
    public async Task LiveCapacityRejectsStaleGenerationAndUnrelatedOfficialIdentity()
    {
        var account = await AuthorizeAsync();
        _native.SetStorage("""{"used":"1 B","total":"5 B"}""");
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", (await CapacityAsync(account with {AccountVersion = "old"})).Problem);
        Assert.AreEqual(0, _native.CapacityRequests);

        _native.Approve(account.AccountId!, "different-provider-user", "account", "files:write");
        var changed = await CapacityAsync(account);
        Assert.AreEqual("unavailable", changed.Status);
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", changed.Problem);
        Assert.IsNull(changed.CheckedAt);
        Assert.IsNull(changed.UsedBytes);
    }

    private Task<PikPakCliCapacityViewModel> CapacityAsync(PikPakCliAccountViewModel account) =>
        _accounts.GetCapacityAsync(account.AccountId, account.AccountVersion, default);

    private async Task<PikPakCliAccountViewModel> AuthorizeAsync(string? providerId = "same-provider-user", bool manageFiles = false)
    {
        var session = manageFiles ? await _accounts.StartManageFilesAuthorizationAsync(default) : await _accounts.StartAsync(default);
        _native.Approve(session.Id, providerId, "account", manageFiles ? "files:manage" : "files:write");
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        return await _accounts.GetAsync(default);
    }

    private Task<PikPakCliAccountViewModel> SetCleanupAsync(PikPakCliAccountViewModel account, bool enabled) =>
        _accounts.SetCleanupSettingsAsync(new(account.AccountId!, account.AccountVersion!, enabled), default);

    private static async Task RejectAsync(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsExceptionAsync<PikPakCliException>(action);
        Assert.AreEqual(code, error.Code);
    }

    private sealed class Native : IPikPakCliProcess
    {
        private readonly Dictionary<string, (string? Identity, string[] Scopes)> _approved = new();
        private readonly Dictionary<string, string> _requestedScopes = new();
        internal List<(string Profile, string Command)> Calls { get; } = [];
        internal HashSet<string> FailStatusOnce { get; } = [];
        internal bool ProfileBusy { get; set; }
        internal int CapacityRequests { get; private set; }
        private JsonElement? _storage;
        internal void SetStorage(string value)
        {
            using var json = JsonDocument.Parse(value);
            _storage = json.RootElement.Clone();
        }

        public async Task<JsonElement?> TryReadStatusAsync(string profile, CancellationToken ct)
        {
            CapacityRequests++;
            return ProfileBusy ? null : await RunAsync(profile, ["auth", "status"], ct);
        }

        internal void Approve(string profile, string? identity, params string[] scopes) => _approved[profile] = (identity, scopes);
        internal string RequestedScope(string profile) => _requestedScopes[profile];

        public Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
            bool payload = false, Action? observeProgress = null)
        {
            var id = Path.GetFileName(profile);
            Calls.Add((id, string.Join(' ', arguments.Take(2))));
            var approved = _approved.TryGetValue(id, out var response);
            object result;
            switch (arguments[1])
            {
                case "login" when arguments.Contains("--continue"):
                    result = approved ? new {status = "success", scopes = response.Scopes} : new {status = "pending"};
                    break;
                case "login":
                    _requestedScopes[id] = arguments[arguments.ToList().IndexOf("--scope") + 1];
                    result = new {device_code = "synthetic-device-code", user_code = "ABCD-EFGH",
                        verification_uri = "https://mypikpak.com/drive/activate", expires_in = 300, interval = 2};
                    break;
                case "status":
                    if (FailStatusOnce.Remove(id)) throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
                    result = new {logged_in = approved, name = "Same display name", user_id = response.Identity, storage = _storage};
                    break;
                default:
                    throw new AssertFailedException("Unexpected native command.");
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
