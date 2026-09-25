using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Service.Models.Input.DataSync;

// Request bodies of the /data-sync endpoints that are not records of the data sync module itself (spec §10.1).

/// <summary>Body of <c>POST /data-sync/links/{id}/resume</c>.</summary>
public record DataSyncLinkResumeInputModel
{
    /// <summary>What the person chose on the paused link; <see cref="DataSyncResumeAction.AskAccessAgain"/> creates access.</summary>
    public DataSyncResumeAction Action { get; set; } = DataSyncResumeAction.Resume;
}

/// <summary>Body of <c>POST /data-sync/sync-now</c>.</summary>
public record DataSyncSyncNowInputModel
{
    /// <summary>One link; null syncs every link.</summary>
    public int? LinkId { get; set; }
}

/// <summary>Body of <c>PUT /data-sync/paused</c>.</summary>
public record DataSyncPausedInputModel
{
    public bool Paused { get; set; }
}

/// <summary>Body of <c>POST /data-sync/restore</c>.</summary>
public record DataSyncRestoreChoiceInputModel
{
    public DataSyncRestoreChoice Choice { get; set; }

    /// <summary>The link a restore was suspected through; null for every link (spec §5.6).</summary>
    public int? LinkId { get; set; }
}
