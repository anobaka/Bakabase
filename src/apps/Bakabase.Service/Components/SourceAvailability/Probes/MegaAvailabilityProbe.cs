using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

public sealed class MegaAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    internal const string ClientName = "Mega.PublicShareMetadata";
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "mega";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        var share = MegaPublicShare.Parse(source.Url, source.AccessCode);
        if (share == null) return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = clients.CreateClient(ClientName);
            var response = await share.CheckAsync(client, timeout.Token);
            return Result(response.Status, response.Reason);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (Exception error) when (error is IOException or HttpRequestException)
        { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
    }

    private static SourceAvailabilityResult Result(string status, string reason) =>
        AvailabilityProbeResults.Create("mega.public-metadata", status, reason, new() {["scope"] = "anonymous", ["response"] = reason});
}
