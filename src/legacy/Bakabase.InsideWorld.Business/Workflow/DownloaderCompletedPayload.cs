namespace Bakabase.InsideWorld.Business.Workflow;

/// <summary>
/// Event payload published on <see cref="DownloaderWorkflowKinds.TriggerCompleted"/>. One
/// payload per completed download task — the trigger emits it as the chain's only item
/// (single-item chains are valid: filters/actions still run once).
/// </summary>
public record DownloaderCompletedPayload
{
    public int TaskId { get; init; }

    /// <summary>Numeric value of <see cref="Bakabase.InsideWorld.Models.Constants.ThirdPartyId"/>.</summary>
    public int ThirdPartyId { get; init; }

    /// <summary>Downloader-specific task type (e.g. <c>ExHentaiDownloadTaskType.SingleWork</c>).</summary>
    public int Type { get; init; }

    /// <summary>The key that drove the download (gallery URL, illust id, etc.).</summary>
    public string Key { get; init; } = "";

    public string? Name { get; init; }
    public string? DownloadPath { get; init; }
}
