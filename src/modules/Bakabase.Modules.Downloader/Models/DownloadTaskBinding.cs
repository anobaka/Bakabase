namespace Bakabase.Modules.Downloader.Models;

/// <summary>Trusted internal registration; clients cannot assign ownership in transfer creation requests.</summary>
public sealed record DownloadTaskBinding
{
    public required string Provider { get; init; }
    public required string ProviderTaskId { get; init; }
    public string Category { get; init; } = "general";
    public string? ParentId { get; init; }
    public int? SiteTaskId { get; init; }
    public string OriginKind { get; init; } = "manual";
    public string? OriginId { get; init; }
    public int? WorkflowRunId { get; init; }
    public int? WorkflowDefinitionId { get; init; }
    public int? DownloadResultId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public string Id => DownloadTaskIdentity.Create(Provider, ProviderTaskId);
}

public static class DownloadTaskIdentity
{
    public static string Create(string provider, string providerTaskId)
    {
        if (provider is not ("site" or "bt" or "http") || string.IsNullOrWhiteSpace(providerTaskId) ||
            providerTaskId.Contains(':')) throw new ArgumentException("Invalid download task identity.");
        return $"{provider}:{providerTaskId}";
    }
}
