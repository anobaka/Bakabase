namespace Bakabase.Service.Components.Federation;

/// <summary>An owned metadata directory. ServerPath is the actual prefix saved in a management mapping.</summary>
public sealed record ManagedLibraryMappingTreeNode(string ServerPath, string Name, long ResourceCount, bool HasChildren);
public sealed record ManagedLibraryMappingTreePage(string LibraryEpoch, ManagedLibraryMappingTreeNode[] Nodes, string? NextCursor);
