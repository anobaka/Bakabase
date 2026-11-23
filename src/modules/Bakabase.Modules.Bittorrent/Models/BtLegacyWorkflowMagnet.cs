namespace Bakabase.Modules.Bittorrent.Models;

/// <summary>Exact immutable dispatch from an older workflow-owned magnet download.</summary>
public sealed record BtLegacyWorkflowMagnet(string JobId, string OperationId, int Attempt,
    string MagnetUri, string OutputDirectory);
