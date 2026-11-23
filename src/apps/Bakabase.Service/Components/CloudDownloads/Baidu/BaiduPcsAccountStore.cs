using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed record BaiduPcsAccountViewModel(string Id, string Name, string? Uid, bool IsDefault,
    bool IsLoggedIn, DateTimeOffset? LastValidatedAt)
{
    public BaiduDownloadOptions DownloadOptions { get; init; } = new();
    public BaiduMembershipViewModel Membership { get; init; } = new();
    public int EffectiveMaxConcurrentTasks => BaiduDownloadPolicy.Evaluate(this, DateTimeOffset.UtcNow).MaxConcurrentTasks;
    public int EffectiveConnectionsPerFile => BaiduDownloadPolicy.Evaluate(this, DateTimeOffset.UtcNow).ConnectionsPerFile;
}

public sealed class BaiduPcsLoginInputModel
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Cookies { get; set; }
    public string? Bduss { get; set; }
    public string? Stoken { get; set; }
}

/// <summary>Account changes are exclusive. Transfers and downloads share the account's read-only profile.</summary>
public sealed class BaiduPcsAccountStore(AppService app, BaiduPcsBridgeClient bridge, BaiduAccountSnapshot? snapshot = null)
{
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly ConcurrentDictionary<string, BaiduAccountLease> _accountLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transferLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _membershipLocks = new();
    private readonly ConcurrentDictionary<string, BaiduDownloadAdmission> _downloadAdmission = new();
    private readonly BaiduAccountSnapshot _snapshot = snapshot ?? new();
    private string Root => Path.Combine(app.AppDataDirectory, "cloud-downloads", "baidu");
    private string Metadata => Path.Combine(Root, "accounts.json");

    private static void PrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, (UnixFileMode)0x1c0);
    }

    private async Task<List<BaiduPcsAccountViewModel>> Read(CancellationToken ct)
    {
        var rows = File.Exists(Metadata)
            ? JsonSerializer.Deserialize<List<BaiduPcsAccountViewModel>>(await File.ReadAllTextAsync(Metadata, ct), BaiduPcsBridgeClient.Json) ?? []
            : [];
        // Existing account files predate both fields. Nulls never enable parallel downloads.
        rows = rows.Select(row => row with { DownloadOptions = row.DownloadOptions ?? new(), Membership = row.Membership ?? new() }).ToList();
        _snapshot.Update(rows);
        return rows;
    }

    private async Task Write(List<BaiduPcsAccountViewModel> rows, CancellationToken ct)
    {
        PrivateDirectory(Root);
        var temporary = Metadata + ".new";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(rows, BaiduPcsBridgeClient.Json), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, (UnixFileMode)0x180);
        File.Move(temporary, Metadata, true);
        _snapshot.Update(rows);
        foreach (var admission in _downloadAdmission.Values) admission.NotifyChanged();
    }

    public async Task<IReadOnlyList<BaiduPcsAccountViewModel>> ListAsync(CancellationToken ct)
    {
        await _metadataLock.WaitAsync(ct);
        try { return await Read(ct); } finally { _metadataLock.Release(); }
    }

    public async Task<string?> ResolveAccountIdAsync(string? id, CancellationToken ct) => !string.IsNullOrWhiteSpace(id)
        ? id : (await ListAsync(ct)).FirstOrDefault(a => a.IsDefault && a.IsLoggedIn)?.Id;

    public BaiduPcsDownloadLimits GetCachedDownloadLimits(string? accountId) => _snapshot.GetCachedDownloadLimits(accountId);

    public async Task<BaiduPcsDownloadLimits> GetDownloadLimitsAsync(string? accountId, CancellationToken ct)
    { await ListAsync(ct); return GetCachedDownloadLimits(accountId); }

    public async Task<T> WithAccountAsync<T>(string id, Func<string, BaiduPcsAccountViewModel, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        using var lease = await _accountLocks.GetOrAdd(id, _ => new()).AcquireAsync(true, ct);
        var row = (await ListAsync(ct)).SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
        var config = Path.Combine(Root, id); PrivateDirectory(config);
        return await action(config, row);
    }

    public async Task<T> WithReadAccountAsync<T>(string id, Func<string, BaiduPcsAccountViewModel, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        using var lease = await _accountLocks.GetOrAdd(id, _ => new()).AcquireAsync(false, ct);
        var profile = await ReadInspectionProfileAsync(id, ct);
        return await action(profile.Directory, profile.Account);
    }

    public async Task<T> WithDownloadAccountAsync<T>(string id, Func<string, BaiduPcsAccountViewModel, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        await ListAsync(ct);
        using var slot = await _downloadAdmission.GetOrAdd(id, _ => new())
            .AcquireAsync(() => GetCachedDownloadLimits(id).MaxConcurrentTasks, ct);
        return await WithReadAccountAsync(id, action, ct);
    }

    internal async Task<T> WithTransferAccountAsync<T>(string id, Func<string, BaiduPcsAccountViewModel, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        // Serialize remote writes before taking the profile lease so queued transfers do not hold up login/logout.
        var gate = _transferLocks.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return await WithReadAccountAsync(id, action, ct); }
        finally { gate.Release(); }
    }

    // Inspection must not wait behind an hours-long download, chmod/create the profile,
    // refresh cookies or switch accounts. The bridge binds this UID and checks a stable
    // read of the existing config; concurrent login replacement is rejected conservatively.
    internal async Task<(string Directory, BaiduPcsAccountViewModel Account)> ReadInspectionProfileAsync(
        string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        var row = (await ListAsync(ct)).SingleOrDefault(a => a.Id == id)
            ?? throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
        if (!row.IsLoggedIn || string.IsNullOrWhiteSpace(row.Uid)) throw new CloudProviderException("BAIDU_LOGIN_REQUIRED");
        var directory = Path.Combine(Root, id);
        if (!Directory.Exists(directory) || !File.Exists(Path.Combine(directory, "pcs_config.json")))
            throw new CloudProviderException("BAIDU_CONFIG_INVALID");
        return (directory, row);
    }

    private async Task Update(string id, Func<BaiduPcsAccountViewModel, BaiduPcsAccountViewModel> update, CancellationToken ct)
    {
        await _metadataLock.WaitAsync(ct);
        try
        {
            var rows = await Read(ct); var index = rows.FindIndex(a => a.Id == id);
            if (index < 0) throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
            rows[index] = update(rows[index]); await Write(rows, ct);
        }
        finally { _metadataLock.Release(); }
    }

    public async Task<BaiduPcsAccountViewModel> LoginAsync(BaiduPcsLoginInputModel input, CancellationToken ct)
    {
        var id = input.Id ?? Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(id, "N", out _) || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100)
            throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        if ((input.Cookies?.Length ?? 0) + (input.Bduss?.Length ?? 0) + (input.Stoken?.Length ?? 0) > 65536)
            throw new ArgumentException("BAIDU_CREDENTIALS_INVALID");
        using var lease = await _accountLocks.GetOrAdd(id, _ => new()).AcquireAsync(true, ct);
        var staging = Path.Combine(Root, id + ".login");
        try
        {
            var existing = (await ListAsync(ct)).SingleOrDefault(a => a.Id == id);
            if (input.Id != null && existing == null) throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
            PrivateDirectory(Root); PrivateDirectory(staging);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await bridge.ExecuteAsync(staging, new { command = "login", input.Cookies, input.Bduss, input.Stoken }, null, timeout.Token);
            var uid = result.GetProperty("uid").GetString();
            if (existing?.Uid != null && existing.Uid != uid) throw new ArgumentException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
            var config = Path.Combine(Root, id); PrivateDirectory(config);
            File.Move(Path.Combine(staging, "pcs_config.json"), Path.Combine(config, "pcs_config.json"), true);
            var row = new BaiduPcsAccountViewModel(id, input.Name.Trim(), uid, existing?.IsDefault ?? false, true, DateTimeOffset.UtcNow)
            { DownloadOptions = existing?.DownloadOptions ?? new() };
            row = row with { Membership = await ProbeMembershipAsync(config, row, ct) };
            await _metadataLock.WaitAsync(ct);
            try
            {
                var rows = await Read(ct);
                // Download settings/default selection do not take the account mutation
                // lease. Preserve changes committed while upstream login was in flight.
                row = MergeLoginMetadata(row, rows.SingleOrDefault(a => a.Id == id));
                rows.RemoveAll(a => a.Id == id);
                if (rows.All(a => !a.IsDefault)) row = row with { IsDefault = true };
                rows.Add(row); await Write(rows, ct);
            }
            finally { _metadataLock.Release(); }
            return row;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    internal static BaiduPcsAccountViewModel MergeLoginMetadata(BaiduPcsAccountViewModel login,
        BaiduPcsAccountViewModel? latest) => latest == null ? login : login with
    {
        DownloadOptions = latest.DownloadOptions,
        IsDefault = latest.IsDefault
    };

    public Task<BaiduPcsAccountViewModel> ValidateAsync(string id, CancellationToken ct) => WithAccountAsync(id, async (config, row) =>
    {
        if (!row.IsLoggedIn) throw new InvalidOperationException("BAIDU_LOGIN_REQUIRED");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var result = await bridge.ExecuteAsync(config, new { command = "validate" }, null, timeout.Token);
            if (result.GetProperty("uid").GetString() != row.Uid) throw new InvalidOperationException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
            var membership = await ProbeMembershipAsync(config, row, ct);
            await Update(id, latest => latest with { LastValidatedAt = DateTimeOffset.UtcNow, Membership = membership }, ct);
            return (await ListAsync(ct)).Single(a => a.Id == id);
        }
        catch (Exception e) when (e.Message == "BAIDU_SESSION_EXPIRED")
        { await Update(id, a => a with { IsLoggedIn = false }, ct); throw; }
    }, ct);

    public async Task<BaiduPcsAccountViewModel> SetDownloadOptionsAsync(string id, BaiduDownloadOptions options, CancellationToken ct)
    {
        if (options.MaxConcurrentTasks is < 1 or > BaiduDownloadOptions.MaximumConcurrentTasks ||
            options.ConnectionsPerFile is < 1 or > BaiduDownloadOptions.MaximumConnectionsPerFile)
            throw new ArgumentException("BAIDU_DOWNLOAD_OPTIONS_INVALID");
        await Update(id, row => row with { DownloadOptions = options }, ct);
        return (await ListAsync(ct)).Single(a => a.Id == id);
    }

    public async Task<BaiduPcsAccountViewModel> RefreshMembershipAsync(string id, CancellationToken ct, bool force = true)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        var gate = _membershipLocks.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var row = (await ListAsync(ct)).SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
            var now = DateTimeOffset.UtcNow;
            if (!force && (row.Membership.FailureCode == null && row.Membership.IsFresh(now) ||
                row.Membership.LastAttemptAt is { } attempt && attempt <= now && now - attempt < TimeSpan.FromMinutes(10))) return row;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                return await WithReadAccountAsync(id, async (directory, current) =>
                {
                    var membership = await ProbeMembershipAsync(directory, current, timeout.Token);
                    await Update(id, latest => latest with { Membership = membership }, ct);
                    return (await ListAsync(ct)).Single(a => a.Id == id);
                }, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await Update(id, latest => latest with { Membership = latest.Membership with
                    { LastAttemptAt = now, FailureCode = "BAIDU_MEMBERSHIP_TIMEOUT" } }, ct);
                return (await ListAsync(ct)).Single(a => a.Id == id);
            }
        }
        finally { gate.Release(); }
    }

    private async Task<BaiduMembershipViewModel> ProbeMembershipAsync(string directory, BaiduPcsAccountViewModel row, CancellationToken ct)
    {
        var attemptedAt = DateTimeOffset.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var result = await bridge.ExecuteAsync(directory, new { command = "membership", expectedUid = row.Uid }, null, timeout.Token);
            if (result.GetProperty("uid").GetString() != row.Uid) throw new InvalidOperationException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
            var kind = result.GetProperty("membershipKind").GetString();
            if (kind is not ("free" or "vip" or "svip")) throw new InvalidOperationException("BAIDU_MEMBERSHIP_RESPONSE_INVALID");
            return new() { Kind = kind, CheckedAt = DateTimeOffset.UtcNow, LastAttemptAt = attemptedAt };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return row.Membership with { LastAttemptAt = attemptedAt, FailureCode = "BAIDU_MEMBERSHIP_TIMEOUT" }; }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Only stable, explicitly supported codes leave the capability; no tokens, URLs or raw bodies.
            var code = error.Message is "BAIDU_SESSION_EXPIRED" or "BAIDU_ACCOUNT_IDENTITY_CHANGED" or
                "BAIDU_MEMBERSHIP_NETWORK_ERROR" or "BAIDU_MEMBERSHIP_RESPONSE_INVALID" or "BAIDU_MEMBERSHIP_UNKNOWN"
                ? error.Message : "BAIDU_MEMBERSHIP_CHECK_FAILED";
            return row.Membership with { LastAttemptAt = attemptedAt, FailureCode = code };
        }
    }

    public async Task SetDefaultAsync(string id, CancellationToken ct)
    {
        await _metadataLock.WaitAsync(ct);
        try
        {
            var rows = await Read(ct); if (rows.All(a => a.Id != id)) throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
            await Write(rows.Select(a => a with { IsDefault = a.Id == id }).ToList(), ct);
        }
        finally { _metadataLock.Release(); }
    }

    public async Task LogoutAsync(string id, bool delete, CancellationToken ct) => await WithAccountAsync(id, async (config, _) =>
    {
        File.Delete(Path.Combine(config, "pcs_config.json"));
        await _metadataLock.WaitAsync(ct);
        try
        {
            var rows = await Read(ct);
            if (delete) rows.RemoveAll(a => a.Id == id);
            else rows = rows.Select(a => a.Id == id ? a with { IsLoggedIn = false } : a).ToList();
            await Write(rows, ct);
        }
        finally { _metadataLock.Release(); }
        if (delete) Directory.Delete(config, true);
        return true;
    }, ct);
}
