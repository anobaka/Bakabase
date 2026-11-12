using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

internal interface IRcloneAccountProfiles
{
    Task<bool> HasAsync(RcloneProfileRef profile, CancellationToken ct);
    Task<string?> VersionAsync(RcloneProfileRef profile, CancellationToken ct);
    Task RemoveAsync(RcloneProfileRef profile, CancellationToken ct);
    Task ConfigurePikPakAsync(string id, string username, string password, CancellationToken ct);
    Task ConfigurePixeldrainAsync(string id, string root, string? key, CancellationToken ct);
    Task ConfigureOneDriveAsync(string id, RcloneOneDriveConfiguration config, CancellationToken ct);
    Task ValidateAsync(RcloneProfileRef profile, CancellationToken ct);
}

/// <summary>Account metadata only. The rclone engine owns the sole refreshable credential configuration.</summary>
public sealed class RcloneAccountStore
{
    private readonly string _directory;
    private readonly IRcloneAccountProfiles _profiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RcloneAccountStore(AppService app, RcloneRemoteEngine engine)
        : this(Path.Combine(app.AppDataDirectory, "cloud-downloads", "rclone-accounts"), new Profiles(engine)) { }
    internal RcloneAccountStore(string directory, IRcloneAccountProfiles profiles)
    { _directory = Path.GetFullPath(directory); _profiles = profiles; }

    public Task<IReadOnlyList<RcloneAccountViewModel>> ListAsync(string? providerId, CancellationToken ct) => Locked(async entries =>
    {
        if (providerId != null) CheckProvider(providerId);
        var result = new List<RcloneAccountViewModel>();
        foreach (var entry in entries.Where(e => providerId == null || e.ProviderId == providerId))
            result.Add(View(entry, await _profiles.HasAsync(Profile(entry), ct)));
        return (IReadOnlyList<RcloneAccountViewModel>) result;
    }, ct);

    public Task<RcloneAccountViewModel> SaveAsync(RcloneAccountInputModel input, CancellationToken ct) => Locked(async entries =>
    {
        CheckProvider(input.ProviderId);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || input.Name.Any(char.IsControl))
            throw new RcloneRemoteException("RCLONE_ACCOUNT_NAME_INVALID");
        var existing = input.Id == null ? null : Find(entries, input.Id);
        if (existing != null && existing.ProviderId != input.ProviderId) throw new RcloneRemoteException("RCLONE_ACCOUNT_INVALID");
        var entry = existing ?? new Entry {Id = Guid.NewGuid().ToString("N"), ProviderId = input.ProviderId};
        var configured = await _profiles.HasAsync(Profile(entry), ct);
        if (input.ProviderId == "pikpak" && (!configured || input.Username != null || input.Password != null))
        {
            if (string.IsNullOrEmpty(input.Username) || string.IsNullOrEmpty(input.Password))
                throw new RcloneRemoteException("RCLONE_CREDENTIAL_INVALID");
            await _profiles.ConfigurePikPakAsync(entry.Id, input.Username, input.Password, ct);
            entry.Problem = null; entry.LastValidatedAt = null;
        }
        if (input.ProviderId == "pixeldrain" && (!configured || input.RootFolderId != null || input.ApiKey != null))
        {
            if (string.IsNullOrEmpty(input.RootFolderId)) throw new RcloneRemoteException("RCLONE_PATH_INVALID");
            await _profiles.ConfigurePixeldrainAsync(entry.Id, input.RootFolderId, input.ApiKey, ct);
            entry.Problem = null; entry.LastValidatedAt = null;
        }
        entry.Name = input.Name.Trim();
        if (existing == null) { entry.IsDefault = !entries.Any(e => e.ProviderId == entry.ProviderId); entries.Add(entry); }
        RclonePrivateStorage.Directory(_directory);
        await SaveEntries(entries, ct);
        return View(entry, await _profiles.HasAsync(Profile(entry), ct));
    }, ct);

    public Task<string> ResolveAccountIdAsync(string providerId, string? requested, CancellationToken ct) => Locked(async entries =>
    {
        CheckProvider(providerId);
        var entry = requested != null ? Find(entries, requested) :
            entries.FirstOrDefault(e => e.ProviderId == providerId && e.IsDefault) ??
            entries.FirstOrDefault(e => e.ProviderId == providerId);
        if (entry == null || entry.ProviderId != providerId || !await _profiles.HasAsync(Profile(entry), ct))
            throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        return entry.Id;
    }, ct);

    public Task<string> GetConfiguredProfileVersionAsync(string providerId, string accountId, CancellationToken ct) => Locked(async entries =>
    {
        CheckProvider(providerId);
        var entry = Find(entries, accountId);
        if (entry.ProviderId != providerId) throw new RcloneRemoteException("RCLONE_ACCOUNT_INVALID");
        return await _profiles.VersionAsync(Profile(entry), ct) ?? throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
    }, ct);

    public Task SetDefaultAsync(string id, CancellationToken ct) => Locked(async entries =>
    {
        var entry = Find(entries, id);
        if (!await _profiles.HasAsync(Profile(entry), ct)) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        foreach (var other in entries.Where(e => e.ProviderId == entry.ProviderId)) other.IsDefault = other.Id == entry.Id;
        await SaveEntries(entries, ct); return true;
    }, ct);

    public Task<RcloneAccountViewModel> ValidateAsync(string id, CancellationToken ct) => Locked(async entries =>
    {
        var entry = Find(entries, id);
        if (!await _profiles.HasAsync(Profile(entry), ct)) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        try { await _profiles.ValidateAsync(Profile(entry), ct); entry.Problem = null; entry.LastValidatedAt = DateTimeOffset.UtcNow; }
        catch (RcloneRemoteException) { entry.Problem = "RCLONE_ACCOUNT_VALIDATION_FAILED"; }
        await SaveEntries(entries, ct); return View(entry, true);
    }, ct);

    public Task DeleteAsync(string id, CancellationToken ct) => Locked(async entries =>
    {
        var entry = Find(entries, id);
        await _profiles.RemoveAsync(Profile(entry), ct);
        entries.Remove(entry);
        if (entry.IsDefault && entries.FirstOrDefault(e => e.ProviderId == entry.ProviderId) is { } next) next.IsDefault = true;
        await SaveEntries(entries, ct); return true;
    }, ct);

    internal Task RequireOneDriveAsync(string id, CancellationToken ct) => Locked(entries =>
    {
        if (Find(entries, id).ProviderId != "oneDrive") throw new RcloneRemoteException("RCLONE_AUTHORIZATION_PROVIDER_INVALID");
        return Task.FromResult(true);
    }, ct);

    internal Task ApplyOneDriveAsync(string id, RcloneOneDriveConfiguration configuration, CancellationToken ct) => Locked(async entries =>
    {
        var entry = Find(entries, id);
        if (entry.ProviderId != "oneDrive") throw new RcloneRemoteException("RCLONE_AUTHORIZATION_PROVIDER_INVALID");
        await _profiles.ConfigureOneDriveAsync(id, configuration, ct);
        entry.Problem = null; entry.LastValidatedAt = DateTimeOffset.UtcNow;
        await SaveEntries(entries, CancellationToken.None); return true;
    }, ct);

    private async Task<T> Locked<T>(Func<List<Entry>, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RcloneRemoteEngine.RejectLinks(_directory);
            var path = Path.Combine(_directory, "accounts.json"); RcloneRemoteEngine.RejectLinks(path);
            var entries = File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(await File.ReadAllTextAsync(path, ct), Json)
                ?? throw new RcloneRemoteException("RCLONE_ACCOUNT_METADATA_INVALID") : [];
            return await action(entries);
        }
        finally { _gate.Release(); }
    }
    private Task SaveEntries(List<Entry> entries, CancellationToken ct) =>
        RclonePrivateStorage.WriteAsync(Path.Combine(_directory, "accounts.json"), JsonSerializer.Serialize(entries, Json), ct);
    private static Entry Find(List<Entry> entries, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new RcloneRemoteException("RCLONE_ACCOUNT_INVALID");
        return entries.SingleOrDefault(e => e.Id == id) ?? throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_FOUND");
    }
    internal static void CheckProvider(string provider)
    { if (provider is not ("pikpak" or "oneDrive" or "pixeldrain")) throw new RcloneRemoteException("RCLONE_ACCOUNT_PROVIDER_INVALID"); }
    private static RcloneProfileRef Profile(Entry entry) => new(entry.ProviderId, entry.Id);
    private static RcloneAccountViewModel View(Entry entry, bool configured) => new()
        {Id = entry.Id, Name = entry.Name, ProviderId = entry.ProviderId, IsConfigured = configured,
            IsDefault = entry.IsDefault, LastValidatedAt = entry.LastValidatedAt, Problem = entry.Problem};
    private sealed class Entry
    {
        public Entry() { }
        public string Id { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public DateTimeOffset? LastValidatedAt { get; set; }
        public string? Problem { get; set; }
    }
    private sealed class Profiles(RcloneRemoteEngine engine) : IRcloneAccountProfiles
    {
        public Task<bool> HasAsync(RcloneProfileRef profile, CancellationToken ct) => engine.HasProfileAsync(profile, ct);
        public Task<string?> VersionAsync(RcloneProfileRef profile, CancellationToken ct) => engine.GetProfileVersionAsync(profile, ct);
        public Task RemoveAsync(RcloneProfileRef profile, CancellationToken ct) => engine.RemoveProfileAsync(profile, ct);
        public Task ConfigurePikPakAsync(string id, string user, string password, CancellationToken ct) => engine.ConfigurePikPakAsync(id, user, password, ct);
        public Task ConfigurePixeldrainAsync(string id, string root, string? key, CancellationToken ct) => engine.ConfigurePixeldrainAsync(id, root, key, ct);
        public Task ConfigureOneDriveAsync(string id, RcloneOneDriveConfiguration config, CancellationToken ct) => engine.ConfigureOneDriveAsync(id, config, ct);
        public async Task ValidateAsync(RcloneProfileRef profile, CancellationToken ct) => _ = await engine.ListAsync(profile, "", ct);
    }
}

internal static class RclonePrivateStorage
{
    internal static void Directory(string path)
    {
        RcloneRemoteEngine.RejectLinks(path); System.IO.Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    internal static async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        RcloneRemoteEngine.RejectLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var output = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(output, new UTF8Encoding(false))) await writer.WriteAsync(content.AsMemory(), ct);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
