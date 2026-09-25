using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The BTask ids of data sync (spec §8.10.1). The part before a <c>:</c> is also the localization key
/// (<c>BTask_Name_{key}</c>, <c>BTask_Description_{key}</c>, <c>BTask_MessageOnInterruption_{key}</c>).
/// </summary>
public static class DataSyncTaskIds
{
    /// <summary>The recurring fetch cycle. It writes no definitions, so it never waits for the enhancer.</summary>
    public const string Fetch = "DataSync";

    /// <summary>Applies every staged pull and every link's once flags (§8.10.2 apply half).</summary>
    public const string Apply = "DataSyncApply";

    /// <summary>The restore choice (§9.5).</summary>
    public const string Restore = "DataSyncRestore";

    public const string ReviewPrefix = "DataSyncReview";
    public const string ResolvePrefix = "DataSyncResolve";
    public const string UndoPrefix = "DataSyncUndo";

    /// <summary>
    /// Every task that writes definitions shares these keys, so they serialize with each other, with the enhancer's
    /// BTask and with path-mark sync, which rewrite options through whole-row writes (§8.10.1, F13, F28).
    /// </summary>
    public static IReadOnlyList<string> WriteConflictKeys { get; } =
        [Apply, "Enhancement", "SyncResources", "SyncPathMarks"];

    public static string Review(string reviewId) => $"{ReviewPrefix}:{reviewId}";
    public static string Resolve(string batchId) => $"{ResolvePrefix}:{batchId}";
    public static string Undo(int applyLogId) => $"{UndoPrefix}:{applyLogId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The localization key of a task id: the id without its <c>:{suffix}</c>.</summary>
    public static string NameKey(string taskId)
    {
        var colon = taskId.IndexOf(':');
        return colon < 0 ? taskId : taskId[..colon];
    }

    /// <summary>True for every data sync task that writes definitions (ApplyInProgress, §8.10.3).</summary>
    public static bool IsWriteTask(string taskId) =>
        NameKey(taskId) is Apply or Restore or ReviewPrefix or ResolvePrefix or UndoPrefix;

    /// <summary>True for any data sync task id, the fetch cycle included.</summary>
    public static bool IsDataSyncTask(string taskId) =>
        string.Equals(taskId, Fetch, StringComparison.Ordinal) || IsWriteTask(taskId);
}
