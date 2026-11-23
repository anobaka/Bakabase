using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionStageDirectoryCleanupReceipt(string Id, int TaskId, int Revision, string PartId,
    string Stage, string Directory, string BindingFingerprint, DateTime StartedAt, string Status,
    IReadOnlyList<AcquisitionTaskDeleteCleanupFile> Files, ArtifactDeliveryCleanupConsent Consent, DateTime? FinishedAt = null)
{
    public AcquisitionCleanupScopes Scopes { get; init; }
    public string? LiveTaskVersion { get; init; }
    public string? SelectionId { get; init; }
    public bool CleanupIntermediates { get; init; }
    public IReadOnlyList<DeferredDirectoryIdentity>? WaitingDirectories { get; init; }
    public int? RemovedFiles { get; init; }
    public long? RemovedBytes { get; init; }

    internal static async Task SaveAsync(string directory, AcquisitionStageDirectoryCleanupReceipt receipt, CancellationToken ct)
    {
        ArtifactManifestFiles.RejectLinks(directory);
        if (OperatingSystem.IsWindows()) System.IO.Directory.CreateDirectory(directory);
        else System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, receipt.Id + ".json"); var pending = path + ".tmp";
        ArtifactManifestFiles.RejectLinks(path); ArtifactManifestFiles.RejectLinks(pending);
        var options = new FileStreamOptions {Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough};
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var stream = new FileStream(pending, options))
        {
            await JsonSerializer.SerializeAsync(stream, receipt, JsonSerializerOptions.Web, ct);
            await stream.FlushAsync(ct); stream.Flush(true);
        }
        File.Move(pending, path, true);
    }
}
