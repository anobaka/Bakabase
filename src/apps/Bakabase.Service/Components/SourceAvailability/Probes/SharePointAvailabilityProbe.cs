using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

public sealed class SharePointAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    private const int Version = 2;
    public int CacheVersion => Version;
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "oneDrive";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !uri.IdnHost.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/:f:/[gs]/[^?#]+\z"))
            return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = clients.CreateClient(PublicShareDownloadClients.SharePoint);
            await SharePointCloudProvider.ProbePublicRootAsync(source.Url, client, deadline.Token);
            return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (HttpRequestException) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (CloudProviderException error)
        {
            return error.Message.StartsWith("SHAREPOINT_PUBLIC_ACCESS_REQUIRED", StringComparison.Ordinal)
                ? Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED")
                : Result(SourceAvailabilityStatuses.Unknown, "SOURCE_AVAILABILITY_UNDETERMINED");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED"); }
    }

    private static SourceAvailabilityResult Result(string status, string reason) =>
        AvailabilityProbeResults.Create("sharepoint.public-folder-metadata", status, reason,
            new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
}
