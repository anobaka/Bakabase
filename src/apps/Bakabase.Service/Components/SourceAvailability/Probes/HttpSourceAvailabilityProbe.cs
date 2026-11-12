using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>HEAD is evidence of current accessibility only. Generic HTTP errors cannot establish share deletion.</summary>
public sealed class HttpSourceAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    private const int Version = 2;
    public int CacheVersion => Version;
    public const string ClientName = "Bakabase.SourceAvailability.Http";
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "http" || source.MethodId == "http.native";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)) return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_URL_INVALID");
        try { PublicCloudHttp.EnsureSafeUri(uri); }
        catch (ArgumentException) { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_URL_INVALID"); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return Classify(response.StatusCode, response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentLength, response.Content.Headers.ContentDisposition?.DispositionType);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (HttpRequestException) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
    }

    internal static SourceAvailabilityResult Classify(HttpStatusCode status, string? mediaType, long? length, string? disposition)
    {
        if ((int)status is 401 or 403) return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        if ((int)status == 429 || (int)status >= 500) return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT");
        // HEAD may be unsupported; 404/410 may hide an authenticated resource, signed URL
        // expiry, or CDN routing failure. None is sufficient to bulk-disable the original source.
        if (status != HttpStatusCode.OK || length is null or < 0 || mediaType is null ||
            mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
            mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) && disposition != "attachment")
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_AVAILABILITY_UNDETERMINED");
        return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
    }

    private static SourceAvailabilityResult Result(string status, string reason) =>
        AvailabilityProbeResults.Create("http.public-head", status, reason, new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
}

public sealed class UnsupportedSourceAvailabilityProbe : ISourceAvailabilityProbe
{
    public bool CanHandle(CloudSource source) => true;
    public Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // GoFile's current gallery-dl metadata path creates a guest account (POST /accounts).
        // Rclone share/account discovery can create profiles or redeem sharing access. Neither
        // belongs in an automatic read-only probe; absence of a probe is never invalidity.
        return Task.FromResult(AvailabilityProbeResults.Create("source.no-readonly-probe", SourceAvailabilityStatuses.Unsupported,
            CloudSourceClassifier.Classify(source.Url) == "gofile" ? "SOURCE_PROBE_READONLY_TOKEN_REQUIRED" : "SOURCE_PROBE_UNSUPPORTED"));
    }
}
