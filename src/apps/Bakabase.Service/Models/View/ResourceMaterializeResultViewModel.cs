namespace Bakabase.Service.Models.View;

/// <summary>
/// What happened when a resource was pointed at a path.
/// </summary>
/// <param name="Materialized">
/// False when the path is already owned by another resource and merging was not asked for. Nothing
/// was changed in that case — the client is expected to ask the user and call again.
/// </param>
/// <param name="Path">The standardized path, once it was actually written.</param>
/// <param name="OccupiedByResourceId">
/// The resource already holding the path. Set both when the call was refused and when it merged, so
/// the client can say which resource was absorbed.
/// </param>
/// <param name="OccupiedByResourceName">What that resource is called, so the question can be asked in words.</param>
/// <param name="Merged">True when that resource was absorbed into this one.</param>
public record ResourceMaterializeResultViewModel(
    bool Materialized,
    string? Path,
    int? OccupiedByResourceId,
    string? OccupiedByResourceName,
    bool Merged);
