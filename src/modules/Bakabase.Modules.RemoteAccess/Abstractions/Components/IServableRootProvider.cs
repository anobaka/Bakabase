namespace Bakabase.Modules.RemoteAccess.Abstractions.Components;

/// <summary>
/// Contributes directories whose contents a remote client is allowed to read.
/// Implementations are additive: a path is servable if it sits under any provider's
/// roots.
/// </summary>
public interface IServableRootProvider
{
    Task<IReadOnlyCollection<string>> GetRootsAsync(CancellationToken ct = default);
}
