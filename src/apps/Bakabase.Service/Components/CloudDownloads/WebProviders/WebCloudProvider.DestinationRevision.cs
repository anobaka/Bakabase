using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

public abstract partial class WebCloudProvider
{
    private static string OperationKey(string operationId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationId)));

    private static string[] StateDirectorySegments(string providerId, string operationKey) =>
        ["cloud-downloads", "web", providerId, operationKey];

    public Task<CloudReadiness> InspectDestinationRevisionAsync(CloudDownloadRequest currentExecutionRequest,
        string newDirectory, CancellationToken ct) => Path.IsPathFullyQualified(newDirectory)
        ? InspectUnstartedDownloadAsync(currentExecutionRequest, ct)
        : Task.FromResult(new CloudReadiness(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN"));

    protected async Task<CloudReadiness> InspectUnstartedDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        if (request.Source.ProviderId != ProviderId || request.RemoteEntry != null ||
            !string.IsNullOrWhiteSpace(request.AccountId) || !string.IsNullOrWhiteSpace(request.Source.AccountId) ||
            string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
            return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
        var key = OperationKey(request.OperationId);
        var gate = _operations.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return new(false, "DOWNLOAD_DESTINATION_ACTIVE");
        try
        {
            ValidateShareUrl(ProviderId, request.Source.Url);
            return InspectUnstartedOperation(appData(), ProviderId, key, ct);
        }
        catch (ArgumentException) { return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN"); }
        finally { gate.Release(); }
    }

    // The bridge binds manifest.json to the resolved output directory, before any payload is
    // downloaded. Its completion receipt and atomic .new files are evidence too. Only the
    // regular bootstrap script may precede discovery; never clear even an incomplete journal.
    internal static CloudReadiness InspectUnstartedOperation(string appData, string providerId, string operationKey, CancellationToken ct)
    {
        try
        {
            var directory = ArtifactManifestFiles.Absolute(appData);
            ArtifactManifestFiles.RejectLinks(directory);
            if (!File.GetAttributes(directory).HasFlag(FileAttributes.Directory))
                return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
            foreach (var segment in StateDirectorySegments(providerId, operationKey))
            {
                ct.ThrowIfCancellationRequested();
                var child = Path.Combine(directory, segment);
                ArtifactManifestFiles.RejectLinks(child);
                try
                {
                    var attributes = File.GetAttributes(child);
                    if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
                        return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                {
                    return ArtifactManifestFiles.IsMissingFileConfirmed(appData, child, ct)
                        ? new(true) : new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
                }
                directory = child;
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (Path.GetFileName(entry) != "bridge.py") return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT");
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                    return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN");
            }
            return new(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { return new(false, "DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN"); }
    }
}
