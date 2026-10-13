using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Media;

/// <summary>
/// The most specific saved override wins even when its target is unavailable. Legacy exact roots
/// precede the group root at the same resource-directory level. Never silently try a broader
/// mapping: a missing local copy falls back to authorized source streaming instead.
/// This helper does not authorize playback; callers must still validate the source lease.
/// </summary>
public static class FederationPathMappingResolver
{
    public const int MaximumLocations = 64;

    public static string? MapAsset(FederatedAsset asset, IReadOnlyList<NodePathMapping> mappings)
    {
        var selected = Select(asset.SourceRootId, asset.RelativePath, asset.MappingLocations, mappings);
        return selected == null ? null : MediaPathBoundary.Map(selected.Value.Mapping.LocalPath, selected.Value.RelativePath);
    }

    public static (string? Path, string? Reason) MapLocation(FederatedResourceLocation location,
        IReadOnlyList<NodePathMapping> mappings)
    {
        var selected = Select(location.SourceRootId, location.RelativePath, location.MappingLocations, mappings);
        if (selected == null) return (null, "PathMappingRequired");
        var path = MediaPathBoundary.MapLocation(selected.Value.Mapping.LocalPath, selected.Value.RelativePath, location.IsDirectory);
        return (path, path == null ? "MappedPathUnavailable" : null);
    }

    public static void ValidateLocations(FederatedMappingLocation[]? locations, string? legacyRoot = null, string? legacyRelative = null)
    {
        if (locations == null) return;
        if (locations.Length > MaximumLocations || locations.Any(x => x == null ||
                !NodeRequestSignature.IsIdentifier(x.SourceRootId) ||
                !(x.SourceRootId == legacyRoot || x.SourceRootId.Length == 65 && x.SourceRootId.StartsWith('g') &&
                  x.SourceRootId[1..].All(char.IsAsciiHexDigit)) ||
                x.RelativePath is not { Length: > 0 and <= 4096 }) ||
            locations.Select(x => x.SourceRootId).Distinct(StringComparer.Ordinal).Count() != locations.Length ||
            locations.Length > 0 && legacyRoot != null && !locations.Any(x => x.SourceRootId == legacyRoot && x.RelativePath == legacyRelative))
            throw new FederationQueryException("InvalidPeerResponse", 502);
        string? previous = null;
        foreach (var location in locations)
        {
            if (!CanRepresentRelative(location.RelativePath) || previous != null && previous != "." &&
                !(location.RelativePath.Equals(previous, StringComparison.OrdinalIgnoreCase) ||
                  location.RelativePath.EndsWith("/" + previous, StringComparison.OrdinalIgnoreCase)))
                throw new FederationQueryException("InvalidPeerResponse", 502);
            previous = location.RelativePath;
        }
    }

    public static bool CanRepresentRelative(string relative) => relative == "." ||
        relative.Length is > 0 and <= 4096 && !relative.Contains('\\') && !relative.Contains(':') &&
        relative.Split('/').All(x => x is not ("" or "." or ".."));

    private static (NodePathMapping Mapping, string RelativePath)? Select(string? legacyRoot, string? legacyRelative,
        FederatedMappingLocation[]? locations, IReadOnlyList<NodePathMapping> mappings)
    {
        ValidateLocations(locations, legacyRoot, legacyRelative);
        if (locations is not { Length: > 0 } && legacyRoot != null && legacyRelative != null)
        {
            var exact = mappings.FirstOrDefault(x => x.SourceRootId == legacyRoot);
            if (exact != null) return (exact, legacyRelative);
        }
        foreach (var location in locations ?? [])
        {
            var mapping = mappings.FirstOrDefault(x => x.SourceRootId == location.SourceRootId);
            if (mapping != null) return (mapping, location.RelativePath);
        }
        return null;
    }
}
