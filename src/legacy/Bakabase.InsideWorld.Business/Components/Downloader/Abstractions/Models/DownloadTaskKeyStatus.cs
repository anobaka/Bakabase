using System;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

/// <summary>
/// What the backend knows about one third-party item (identified by its download
/// <see cref="Key"/>): whether it is sitting in the download list right now, and
/// whether it was ever downloaded before.
///
/// The permanent download record alone cannot answer the first question - it is
/// written when a task is created and never removed - so consumers that want to
/// offer "remove it again" (the baka-monkey userscript) need the live task ids too.
/// </summary>
public record DownloadTaskKeyStatus
{
    /// <summary>
    /// The third-party item identifier (gallery url, video id, ...), same value as
    /// <see cref="Models.Db.DownloadTaskDbModel.Key"/>.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Ids of the download tasks currently in the list for this key. Empty when the
    /// item is not queued (never added, or the task was already removed).
    /// </summary>
    public int[] TaskIds { get; set; } = [];

    /// <summary>
    /// Status of the most active task among <see cref="TaskIds"/>
    /// (in progress &gt; failed &gt; disabled &gt; complete), null when there is none.
    /// </summary>
    public DownloadTaskDbModelStatus? Status { get; set; }

    /// <summary>
    /// When this item was last added for download or finished downloading, null when
    /// there is no record of it at all.
    /// </summary>
    public DateTime? DownloadedAt { get; set; }
}
