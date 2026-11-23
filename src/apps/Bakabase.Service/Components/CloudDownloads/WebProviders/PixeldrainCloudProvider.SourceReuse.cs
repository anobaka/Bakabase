using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.WebProviders;

public sealed partial class PixeldrainCloudProvider
{
    public bool CanInspectReuseSource(CloudDownloadRequest request) =>
        request.Source.ProviderId == ProviderId && request.Source.Kind == "share" &&
        request.Source.MethodId is (null or "pixeldrain.galleryDl") && request.RemoteEntry == null &&
        string.IsNullOrWhiteSpace(request.AccountId) && string.IsNullOrWhiteSpace(request.Source.AccountId) &&
        !string.IsNullOrWhiteSpace(request.OperationId) && Path.IsPathFullyQualified(request.OutputDirectory);

    public Task<CloudDownloadReuseInventory> InspectReuseSourceAsync(CloudDownloadRequest request, CancellationToken ct) =>
        CanInspectReuseSource(request) ? InspectBridgeReuseSourceAsync(request, ct)
            : Task.FromException<CloudDownloadReuseInventory>(new CloudProviderException("DOWNLOAD_DESTINATION_REUSE_UNSUPPORTED"));

    public async Task<CloudDownloadWritePlan?> PrepareWriteAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        if (request.ReuseSource is not { } source) return null;
        if (await InspectBridgeReuseDestinationAsync(request, ct) is { } remaining)
            return new(request, [new(request.OutputDirectory, remaining)]);
        if (source.DisableSourceRead)
            throw new CloudProviderException("DOWNLOAD_DESTINATION_REUSE_SOURCE_CHANGED");
        var original = request with
        {
            OutputDirectory = source.OutputDirectory, ProviderLayoutId = source.OperationId, ReuseSource = null
        };
        var inventory = await InspectReuseSourceAsync(original, ct);
        if (inventory.InventoryFingerprint != source.InventoryFingerprint)
            throw new CloudProviderException("DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
        // Seeding and receiving the missing suffix share one file budget. A candidate's
        // local bytes reduce network reads, but they still occupy the destination volume.
        return new(request, [new(request.OutputDirectory, inventory.Estimate.TotalBytes)]);
    }
}
