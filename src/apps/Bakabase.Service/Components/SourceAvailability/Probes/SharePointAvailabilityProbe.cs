using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

public sealed class SharePointAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    private const int Version = 3;
    public int CacheVersion => Version;
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "oneDrive";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        var attachment = SharePointCloudProvider.IsAttachment(source.Url);
        SourceAvailabilityResult Result(string status, string reason) => AvailabilityProbeResults.Create(
            attachment ? "sharepoint.public-attachment-metadata" : "sharepoint.public-folder-metadata", status, reason,
            new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
        if (!SharePointCloudProvider.SupportsPublicLink(source.Url))
            return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = clients.CreateClient(attachment ? PublicShareDownloadClients.SharePointAttachment : PublicShareDownloadClients.SharePoint);
            if (attachment) await SharePointCloudProvider.ReadAttachmentAsync(new Uri(source.Url), client, deadline.Token);
            else await SharePointCloudProvider.ProbePublicRootAsync(source.Url, client, deadline.Token);
            return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (HttpRequestException) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (CloudProviderException error)
        {
            return error.Message.Split(':', 2)[0] switch
            {
                "SHAREPOINT_PUBLIC_ACCESS_REQUIRED" => Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED"),
                "SHAREPOINT_ATTACHMENT_REMOVED" => Result(SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND"),
                "PUBLIC_SHARE_NETWORK_ERROR" => Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"),
                _ => Result(SourceAvailabilityStatuses.Unknown, "SOURCE_AVAILABILITY_UNDETERMINED")
            };
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED"); }
    }

}
