using Bakabase.Modules.Federation.Contracts;
using System.Text.Json.Serialization;

namespace Bakabase.Modules.Federation.Media;

public sealed record ResourceResolveRequest(ResourceRef[] Refs);
public sealed record ResourceResolveResponse(FederatedResourceDetail[] Resources);
public sealed record FederatedProperty(string Label, string Type, object? Value, int Scope);
public sealed record FederatedSource(int Kind, string? Label, string? Url = null);
public sealed record FederatedExternalIdentity(string Provider, string ExternalId);
public sealed record FederatedCollection(string Name, string? Color);
public sealed record AssetRef(ResourceRef ResourceRef, string AssetId);
public sealed record FederatedAsset(string AssetId, string Kind, string FileName, string ContentType,
    string? SourceRootId, string? RelativePath, DateTimeOffset ExpiresAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FederatedMappingLocation[]? MappingLocations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanOpenLocally = null);
public sealed record FederatedResourceDetail(ResourceRef Ref, string OwnerLabel, string DisplayName,
    string? FileName, string Availability, FederatedProperty[] Properties, FederatedSource[] Sources,
    FederatedExternalIdentity[] ExternalIdentities, FederatedCollection[] Collections,
    FederatedAsset[] Assets, string? UnavailableReason, FederationDirectoryAccess? DirectoryAccess = null,
    FederatedResourceLocation? Location = null);
public sealed record FederationDirectoryAccess(bool CanOpen, string? Reason = null);
public sealed record FederatedResourceLocation(string SourceRootId, string RelativePath, bool IsDirectory,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FederatedMappingLocation[]? MappingLocations = null);
/// <summary>Additional directory ancestors, most specific first; legacy root fields stay unchanged.</summary>
public sealed record FederatedMappingLocation(string SourceRootId, string RelativePath);
public sealed record ResourceLocationRequest(ResourceRef ResourceRef);
public sealed record ResourceLocationResponse(ResourceRef Ref, FederatedResourceLocation? Location);
public sealed record OpenResourceDirectoryRequest(ResourceRef ResourceRef);
public sealed record OpenResourceDirectoryResponse(bool Opened);
public sealed record PlaybackSessionRequest(AssetRef AssetRef, string Mode = "preview");
public sealed record PlaybackSessionResponse(string? Url, string ContentType, bool Launched,
    DateTimeOffset ExpiresAt);
public sealed record MappingRoot(string SourceRootId, string Name);
/// <summary>Only directory names and opaque identifiers leave the source. No absolute paths are exported.</summary>
public sealed record MappingTreeNode(string SourceRootId, string Name, long ResourceCount, bool HasChildren,
    string BrowseToken);
public sealed record MappingTreePage(MappingTreeNode[] Nodes, string? NextCursor = null);

/// <summary>Tokens contain no filesystem paths and are scoped to one owner and library incarnation.</summary>
public sealed record AssetLease(string AssetId, ResourceRef ResourceRef, string GrantId, long GrantVersion,
    string Path, string ContentType, string Kind, DateTimeOffset ExpiresAt);
