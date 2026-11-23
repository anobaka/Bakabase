using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>Read one page of Google's public folder listing, also used by gdown. Never follow a child or fetch file contents.</summary>
public sealed class GoogleDriveFolderAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    internal const string ClientName = "Bakabase.SourceAvailability.GoogleDriveFolder";
    private const int Version = 1;
    public int CacheVersion => Version;
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "googleDrive";

    private static readonly Regex FolderPath = new(@"\A/drive/(?:u/[0-9]+/)?folders/(?<id>[A-Za-z0-9_-]{1,128})/?\z");
    private static readonly Regex ResourceKey = new(@"\A[A-Za-z0-9_-]{1,128}\z");
    // The anonymous list identifies each member both in the entry and its link. Unexpected markup is inconclusive.
    private static readonly Regex ListedMember = new(
        """<div class="flip-entry" id="entry-(?<id>[A-Za-z0-9_-]{1,128})"[^<>]{0,256}>\s*<div class="flip-entry-info">\s*<a href="https://(?:drive\.google\.com/(?:file/d|drive/folders)|docs\.google\.com/(?:document|spreadsheets|presentation)/d)/\k<id>(?=[/?"])""",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    internal static bool AllowedEndpoint(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.IdnHost == "drive.google.com" && uri.AbsolutePath == "/embeddedfolderview";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.IdnHost != "drive.google.com" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            FolderPath.Match(uri.AbsolutePath) is not {Success: true} folder)
            return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        var query = QueryHelpers.ParseQuery(uri.Query);
        foreach (var pair in query)
            if (pair.Key is not ("resourcekey" or "usp"))
                return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        var target = "https://drive.google.com/embeddedfolderview?id=" + folder.Groups["id"].Value;
        if (query.TryGetValue("resourcekey", out var keys))
        {
            if (keys.Count != 1 || keys[0] is not {} key || !ResourceKey.IsMatch(key))
                return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
            target += "&resourcekey=" + key;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = response.StatusCode;
            if (status == HttpStatusCode.Unauthorized || (int)status is >= 300 and < 400 &&
                response.Headers.Location is {IsAbsoluteUri: true, Scheme: "https", IdnHost: "accounts.google.com", IsDefaultPort: true} login &&
                login.UserInfo.Length == 0)
                return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
            if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
                return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT");
            // Drive's notFound response deliberately covers both missing objects and missing read permission.
            if (status != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/html")
                return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_AVAILABILITY_UNDETERMINED");
            var body = await PixeldrainAvailabilityProbe.ReadBounded(response.Content, 256 * 1024, timeout.Token);
            var listing = Encoding.UTF8.GetString(body);
            if (listing.Contains("<body class=\"flip-embedded\"", StringComparison.Ordinal) &&
                listing.Contains("""<div class="flip-entries">""", StringComparison.Ordinal) && ListedMember.IsMatch(listing))
                return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_METADATA_INCOMPLETE");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (HttpRequestException) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (Exception error) when (error is InvalidDataException or RegexMatchTimeoutException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_METADATA_INCOMPLETE"); }
    }

    private static SourceAvailabilityResult Result(string status, string reason) =>
        AvailabilityProbeResults.Create("googleDrive.public-folder-metadata", status, reason,
            new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
}
