using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>Official public metadata API only; /d filesystem locators remain a separate capability.</summary>
public sealed class PixeldrainAvailabilityProbe(IHttpClientFactory clients) : ISourceAvailabilityProbe
{
    private const int Version = 2;
    public int CacheVersion => Version;
    public const string ClientName = "Bakabase.SourceAvailability.Pixeldrain";
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "pixeldrain";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        var uri = new Uri(source.Url);
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/(u|l)/[A-Za-z0-9_-]{1,128}/?\z"))
            return Result(SourceAvailabilityStatuses.Unsupported, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
        var parts = uri.AbsolutePath.Split('/');
        var list = parts[1] == "l";
        var id = parts[2];
        // Preserve selected list-item identity; unsupported selectors cannot prove the chosen item exists.
        var fragment = uri.Fragment;
        int? selected = null;
        if (fragment.Length > 0)
        {
            var match = Regex.Match(fragment, @"\A#item=([0-9]{1,5})\z");
            if (!list || !match.Success || !int.TryParse(match.Groups[1].Value, out var index))
                return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_UNSUPPORTED_LOCATOR");
            selected = index;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.GetAsync("https://pixeldrain.com/api/" + (list ? "list/" : "file/") + id +
                (list ? "" : "/info"), HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var body = await ReadBounded(response.Content, 2 * 1024 * 1024, deadline.Token);
            using var document = JsonDocument.Parse(body);
            return Classify(response.StatusCode, document.RootElement, id, list, selected);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (HttpRequestException error) when (AvailabilityProbeResults.RequiresExplicitNetworkConfiguration(error))
        { return Result(SourceAvailabilityStatuses.Unknown, CloudSourceNetworkException.RequirementCode); }
        catch (HttpRequestException) { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (Exception error) when (error is JsonException or InvalidDataException) { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED"); }
    }

    internal static SourceAvailabilityResult Classify(HttpStatusCode status, JsonElement json, string id, bool list, int? selected = null)
    {
        if (json.ValueKind != JsonValueKind.Object) return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED");
        var success = json.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True;
        var code = json.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (status == HttpStatusCode.NotFound && !success && code == "not_found")
            return Result(SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_NOT_FOUND");
        if ((int)status == 429 || (int)status >= 500 || code is "download_limit_exceeded" or "transfer_limit_exceeded" or "max_concurrent_downloads")
            return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT");
        if ((int)status is 401 or 403 || code is "authentication_required" or "forbidden" or "file_rate_limited_captcha_required")
            return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        if (status != HttpStatusCode.OK || !success || !json.TryGetProperty("id", out var actual) || actual.ValueKind != JsonValueKind.String || actual.GetString() != id)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED");
        if (list)
        {
            if (!json.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0 ||
                !json.TryGetProperty("file_count", out var count) || !count.TryGetInt32(out var expected) || expected != files.GetArrayLength())
                return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_METADATA_INCOMPLETE");
            if (selected is { } i)
            {
                if (i < 0 || i >= files.GetArrayLength()) return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_SELECTED_ITEM_UNAVAILABLE");
                return FileState(files[i]);
            }
            foreach (var file in files.EnumerateArray())
            {
                var result = FileState(file);
                // A partial list is never proof that the whole share is invalid.
                if (result.Status != SourceAvailabilityStatuses.Available)
                    return Result(result.Status == SourceAvailabilityStatuses.ConfirmedUnavailable ? SourceAvailabilityStatuses.Unknown : result.Status,
                        "SOURCE_METADATA_INCOMPLETE");
            }
            return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        }
        return FileState(json);
    }

    private static SourceAvailabilityResult FileState(JsonElement file)
    {
        if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !file.TryGetProperty("size", out var size) || !size.TryGetInt64(out var length) || length < 0)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_METADATA_INCOMPLETE");
        // Official metadata uses availability for download restrictions even when
        // HTTP and success both indicate successful metadata retrieval.
        // https://pixeldrain.com/api#file-id-info (reviewed 2026-10-25).
        if (file.TryGetProperty("availability", out var availability) && availability.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(availability.GetString()))
            return availability.GetString() is "download_limit_exceeded" or "transfer_limit_exceeded" or "max_concurrent_downloads"
                ? Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT")
                : Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        if (file.TryGetProperty("abuse_type", out var abuse) && abuse.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(abuse.GetString()))
            return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        if (file.TryGetProperty("can_download", out var permitted) && permitted.ValueKind == JsonValueKind.False)
            return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
    }

    internal static async Task<byte[]> ReadBounded(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("Metadata response limit exceeded.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (await input.ReadAsync(buffer, ct) is var count && count > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Metadata response limit exceeded.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    private static SourceAvailabilityResult Result(string status, string reason) =>
        AvailabilityProbeResults.Create("pixeldrain.public-metadata", status, reason, new() { ["scope"] = "anonymous", ["response"] = reason }, version: Version);
}
