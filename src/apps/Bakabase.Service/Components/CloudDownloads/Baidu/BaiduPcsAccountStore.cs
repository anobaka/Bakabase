using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

public sealed record BaiduPcsAccountViewModel(string Id, string Name, string? Uid, bool IsDefault,
    bool IsLoggedIn, DateTimeOffset? LastValidatedAt);

public sealed class BaiduPcsLoginInputModel
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Cookies { get; set; }
    public string? Bduss { get; set; }
    public string? Stoken { get; set; }
}

/// <summary>Stable account identity, private isolated upstream profiles and serialized account operations.</summary>
public sealed class BaiduPcsAccountStore(AppService app, BaiduPcsBridgeClient bridge)
{
    private readonly SemaphoreSlim _metadataLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountLocks = new();
    private string Root => Path.Combine(app.AppDataDirectory, "cloud-downloads", "baidu");
    private string Metadata => Path.Combine(Root, "accounts.json");

    private static void PrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, (UnixFileMode)0x1c0);
    }

    private async Task<List<BaiduPcsAccountViewModel>> Read(CancellationToken ct) => File.Exists(Metadata)
        ? JsonSerializer.Deserialize<List<BaiduPcsAccountViewModel>>(await File.ReadAllTextAsync(Metadata, ct), BaiduPcsBridgeClient.Json) ?? []
        : [];

    private async Task Write(List<BaiduPcsAccountViewModel> rows, CancellationToken ct)
    {
        PrivateDirectory(Root);
        var temporary = Metadata + ".new";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(rows, BaiduPcsBridgeClient.Json), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, (UnixFileMode)0x180);
        File.Move(temporary, Metadata, true);
    }

    public async Task<IReadOnlyList<BaiduPcsAccountViewModel>> ListAsync(CancellationToken ct)
    {
        await _metadataLock.WaitAsync(ct);
        try { return await Read(ct); } finally { _metadataLock.Release(); }
    }

    public async Task<string?> ResolveAccountIdAsync(string? id, CancellationToken ct) => !string.IsNullOrWhiteSpace(id)
        ? id : (await ListAsync(ct)).FirstOrDefault(a => a.IsDefault && a.IsLoggedIn)?.Id;

    public async Task<T> WithAccountAsync<T>(string id, Func<string, BaiduPcsAccountViewModel, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("BAIDU_ACCOUNT_INVALID");
        var gate = _accountLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var row = (await ListAsync(ct)).SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("BAIDU_ACCOUNT_NOT_FOUND");
            var config = Path.Combine(Root, id); PrivateDirectory(config);
            return await action(config, row);
        }
        finally { gate.Release(); }
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
        var gate = _accountLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(ct);
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
            var row = new BaiduPcsAccountViewModel(id, input.Name.Trim(), uid, existing?.IsDefault ?? false, true, DateTimeOffset.UtcNow);
            await _metadataLock.WaitAsync(ct);
            try
            {
                var rows = await Read(ct); rows.RemoveAll(a => a.Id == id);
                if (rows.All(a => !a.IsDefault)) row = row with { IsDefault = true };
                rows.Add(row); await Write(rows, ct);
            }
            finally { _metadataLock.Release(); }
            return row;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            finally { gate.Release(); }
        }
    }

    public Task<BaiduPcsAccountViewModel> ValidateAsync(string id, CancellationToken ct) => WithAccountAsync(id, async (config, row) =>
    {
        if (!row.IsLoggedIn) throw new InvalidOperationException("BAIDU_LOGIN_REQUIRED");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var result = await bridge.ExecuteAsync(config, new { command = "validate" }, null, timeout.Token);
            if (result.GetProperty("uid").GetString() != row.Uid) throw new InvalidOperationException("BAIDU_ACCOUNT_IDENTITY_CHANGED");
            row = row with { LastValidatedAt = DateTimeOffset.UtcNow };
            await Update(id, _ => row, ct); return row;
        }
        catch (Exception e) when (e.Message == "BAIDU_SESSION_EXPIRED")
        { await Update(id, a => a with { IsLoggedIn = false }, ct); throw; }
    }, ct);

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
