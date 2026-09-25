using System.Text.Json;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Identity;

/// <summary>
/// The private node state is committed as one file so claiming a grant and remembering
/// its peer cannot be torn apart by a crash. No state object is exposed to the UI.
/// </summary>
public sealed class FederationStateStore(IFederationDataDirectory directory, INodeIdSource identitySource)
{
    public const string FileName = "state.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FederationState? _state;

    internal async Task<FederationState> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return Clone(await LoadAsync(ct));
        }
        finally { _gate.Release(); }
    }

    internal async Task<T> MutateAsync<T>(Func<FederationState, T> mutate, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = Clone(await LoadAsync(ct));
            var result = mutate(state);
            await WriteAsync(state, ct);
            _state = state;
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<NodeIdentity> GetIdentityAsync(CancellationToken ct = default)
    {
        var existing = await ReadAsync(ct);
        if (existing.NodeId != null)
            return new NodeIdentity(existing.NodeId, existing.LibraryEpoch!, NameOf(existing));

        // Inherit the old server id once. Importing ordinary legacy options later must
        // not change an established node's identity or strand all of its grants.
        var nodeId = await identitySource.GetNodeIdAsync(ct);
        if (!NodeRequestSignature.IsIdentifier(nodeId))
            throw new FederationAccessException("InvalidNodeIdentity", 503, "The host has no valid persistent node identity.");

        return await MutateAsync(state =>
        {
            state.NodeId ??= nodeId;
            state.LibraryEpoch ??= Guid.NewGuid().ToString("N");
            return new NodeIdentity(state.NodeId, state.LibraryEpoch, NameOf(state));
        }, ct);
    }

    public const int MaxDisplayNameLength = 64;
    public const string DisplayNameVariable = "BAKABASE_NODE_NAME";

    /// <summary>A headless host may name itself through the environment; otherwise the user's choice wins.</summary>
    private static string NameOf(FederationState state) =>
        Normalize(Environment.GetEnvironmentVariable(DisplayNameVariable)) ?? state.DisplayName ?? Environment.MachineName;

    public static string? Normalize(string? name)
    {
        var value = name?.Trim();
        return string.IsNullOrEmpty(value) || value.Length > MaxDisplayNameLength || value.Any(char.IsControl)
            ? null : value;
    }

    public Task SetDisplayNameAsync(string? name, CancellationToken ct = default)
    {
        var value = Normalize(name);
        if (name?.Trim() is { Length: > 0 } && value == null)
            throw new FederationAccessException("InvalidDeviceName", 400,
                $"Use a device name of at most {MaxDisplayNameLength} printable characters.");
        return MutateAsync(state => { state.DisplayName = value; return true; }, ct);
    }

    public async Task<bool> IsSharingEnabledAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).SharingEnabled;

    /// <summary>Whether devices this one approved may read its definitions (<c>datasync.read</c>).</summary>
    public async Task<bool> IsDataSyncSharingEnabledAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).DataSyncSharingEnabled;

    /// <summary>Both sharing switches from one read, as the node gate checks them before authentication (§7.3).</summary>
    public async Task<FederationSharingSwitches> GetSharingSwitchesAsync(CancellationToken ct = default)
    {
        var state = await ReadAsync(ct);
        return new FederationSharingSwitches(state.SharingEnabled, state.DataSyncSharingEnabled);
    }

    public async Task<bool> IsBrowsingEnabledAsync(CancellationToken ct = default) =>
        (await ReadAsync(ct)).BrowsingEnabled;

    public Task SetBrowsingEnabledAsync(bool enabled, CancellationToken ct = default) =>
        MutateAsync(state => { state.BrowsingEnabled = enabled; return true; }, ct);

    /// <summary>Only the explicit local clone/reset command may replace unreadable node state.</summary>
    internal async Task<NodeIdentity> ResetAsNewNodeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = new FederationState
            {
                NodeId = Guid.NewGuid().ToString("N"),
                LibraryEpoch = Guid.NewGuid().ToString("N")
            };
            await WriteAsync(state, ct);
            _state = state;
            return new NodeIdentity(state.NodeId, state.LibraryEpoch, NameOf(state));
        }
        finally { _gate.Release(); }
    }

    private async Task<FederationState> LoadAsync(CancellationToken ct)
    {
        if (_state != null) return _state;
        var path = System.IO.Path.Combine(directory.Path, FileName);
        if (!File.Exists(path)) return _state = new FederationState();
        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<FederationState>(stream, Json, ct);
            if (state == null || state.SchemaVersion != 1 ||
                (state.NodeId == null) != (state.LibraryEpoch == null) ||
                state.NodeId != null && (!NodeRequestSignature.IsIdentifier(state.NodeId) ||
                                        !NodeRequestSignature.IsIdentifier(state.LibraryEpoch!)) ||
                state.Peers == null || state.InboundGrants == null || state.OutboundGrants == null ||
                state.IncomingRequests == null || state.OutgoingRequests == null)
                throw new JsonException("Invalid node state schema.");
            // Added for data sync: absent from older files, so missing or null is simply empty.
            state.InboundDataSyncGrants ??= new(StringComparer.Ordinal);
            state.OutboundDataSyncGrants ??= new(StringComparer.Ordinal);
            state.IncomingDataSyncRequests ??= [];
            state.OutgoingDataSyncRequests ??= [];
            state.DataSyncReciprocalInvitations ??= [];
            return _state = state;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            throw new FederationAccessException("SharingStateUnavailable", 503,
                "The node sharing state cannot be read. Restore it or explicitly reset sharing; it was not overwritten.");
        }
    }

    private async Task WriteAsync(FederationState state, CancellationToken ct)
    {
        var root = directory.Ensure();
        var path = System.IO.Path.Combine(root, FileName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await JsonSerializer.SerializeAsync(stream, state, Json, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static FederationState Clone(FederationState state) =>
        JsonSerializer.Deserialize<FederationState>(JsonSerializer.SerializeToUtf8Bytes(state, Json), Json)!;
}

/// <param name="Library">Library sharing: <c>library.read</c> grants may read.</param>
/// <param name="DataSync">Definitions sharing: <c>datasync.read</c> grants may read.</param>
public readonly record struct FederationSharingSwitches(bool Library, bool DataSync);
