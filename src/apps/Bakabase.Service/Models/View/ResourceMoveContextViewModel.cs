namespace Bakabase.Service.Models.View;

/// <summary>The library served by this endpoint, including when managed from another computer.</summary>
public sealed record ResourceMoveContextViewModel(string NodeId, string LibraryEpoch);
