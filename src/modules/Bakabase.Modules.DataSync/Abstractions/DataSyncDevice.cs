namespace Bakabase.Modules.DataSync.Abstractions;

/// <summary>This install's identity in data sync (§2.11): the federation node, its library epoch and its name.</summary>
public sealed record DataSyncDevice(string NodeId, string LibraryEpoch, string Name);

/// <summary>
/// Production: FederationDataSyncDeviceIdentity in the Service, over INodeIdentityProvider. Tests: a per-provider
/// fake. Production never falls back to an invented identity: when the service is missing, resolution fails loudly.
/// </summary>
public interface IDataSyncDeviceIdentity
{
    Task<DataSyncDevice> GetAsync(CancellationToken ct);
}

/// <summary>
/// This install (never a peer). Production follows ServiceSelfDescription.KindOf: WinForms and MacOS are desktop,
/// Docker is headless, otherwise desktop exactly when IManagedServerService is registered (§2.11). It decides only
/// whether this install creates notifications.
/// </summary>
public interface IDataSyncHostKind
{
    bool IsHeadless { get; }
}
