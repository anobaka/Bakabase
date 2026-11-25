using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionTaskDeletePreviewStore
{
    // History of confirmed operations. It contains no paths, file selections or replayable grants.
    private sealed record Receipt(IReadOnlyList<int> TaskIds, AcquisitionTaskDeleteOptions Options, AcquisitionTaskDeleteProgress Progress);
    private readonly Func<string>? _appData;
    private readonly ILogger<AcquisitionTaskDeletePreviewStore>? _logger;

    public AcquisitionTaskDeletePreviewStore() { }
    public AcquisitionTaskDeletePreviewStore(AppService app, ILogger<AcquisitionTaskDeletePreviewStore> logger)
        : this(() => app.AppDataDirectory, logger) { }
    internal AcquisitionTaskDeletePreviewStore(Func<string> appData, ILogger<AcquisitionTaskDeletePreviewStore>? logger = null)
    { _appData = appData; _logger = logger; }

    private string ReceiptPath(string id) => AppDataStorage.Acquisition.GetPath(_appData!(), "deletion-receipts", id + ".json");

    private AcquisitionTaskDeleteProgress? ReadReceipt(string id)
    {
        if (_appData == null) return null;
        var path = ReceiptPath(id);
        ArtifactManifestFiles.RejectLinks(path);
        try
        {
            using var stream = File.OpenRead(path);
            var receipt = JsonSerializer.Deserialize<Receipt>(stream, JsonSerializerOptions.Web);
            if (receipt?.Progress is not {} progress || progress.PreviewId != id || progress.CleanupFiles == null ||
                progress.DeleteDeliveredFiles == null || receipt.TaskIds.Count != progress.TotalTasks)
                throw new InvalidDataException("Invalid acquisition deletion receipt.");
            // A running operation always has a live, unexpired owner in this process.
            // Only a stopped owner leaves a nonterminal receipt behind.
            return progress.State == "running"
                ? progress with {State = "interrupted", Phase = "interrupted", ReceiptOnly = true}
                : progress with {ReceiptOnly = true};
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal void Checkpoint(Entry entry)
    {
        if (_appData == null || entry.Progress.State == "running" &&
            entry.Progress.UpdatedAt - entry.LastCheckpoint < TimeSpan.FromSeconds(5)) return;
        try { WriteReceipt(entry, entry.Options!, entry.Progress, first: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A confirmed on-disk receipt still fences replay. A later progress failure must
            // not interrupt already authorized cleanup or discard its in-process outcome.
            _logger?.LogWarning(ex, "Could not update acquisition deletion receipt {OperationId}", entry.Preview.PreviewId);
            entry.LastCheckpoint = entry.Progress.UpdatedAt;
        }
    }

    private void WriteReceipt(Entry entry, AcquisitionTaskDeleteOptions confirmed, AcquisitionTaskDeleteProgress progress, bool first)
    {
        if (_appData == null) return;
        var path = ReceiptPath(progress.PreviewId);
        ArtifactManifestFiles.RejectLinks(path);
        var directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None};
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temp, options))
            {
                JsonSerializer.Serialize(stream, new Receipt(entry.Preview.Rows.Select(r => r.TaskId).ToArray(), confirmed, progress), JsonSerializerOptions.Web);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: !first);
            entry.LastCheckpoint = progress.UpdatedAt;
        }
        finally { File.Delete(temp); }
    }
}
