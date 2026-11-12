using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;

/// <summary>
/// Supplies current scheduling limits from local configuration or cached account capabilities.
/// Implementations must not perform network requests: the same policy projects queued task status.
/// Sources without a policy retain a single task slot.
/// </summary>
public interface IDownloadTaskConcurrencyPolicy
{
    ThirdPartyId ThirdPartyId { get; }
    DownloadTaskConcurrencyLimits GetLimits(int taskId, string? options);
}

/// <summary>
/// Both limits must permit admission. A partition identifies an account (or another source-specific
/// resource); its limit is independent of the aggregate source limit. Lowering a limit only affects
/// new starts and never cancels active downloads.
/// </summary>
public sealed record DownloadTaskConcurrencyLimits(
    int SourceLimit = 1, string? PartitionKey = null, int PartitionLimit = 1);
