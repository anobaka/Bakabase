using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>The public handle identifies the share on MEGA. Its decryption key stays on the client.</summary>
public sealed class MegaPublicShare
{
    public static readonly Uri MetadataEndpoint = new("https://g.api.mega.co.nz/cs?id=0");
    private const int MaximumMetadataBytes = 256 * 1024;
    private static readonly Regex Modern = new(@"\A/(?<kind>file|folder)/(?<handle>[A-Za-z0-9_-]{8})\z");
    private static readonly Regex Legacy = new(@"\A#(?<folder>F)?!(?<handle>[A-Za-z0-9_-]{8})!(?<key>[A-Za-z0-9_-]+)\z");
    private static readonly Regex FileKey = new(@"\A[A-Za-z0-9_-]{43}\z");
    private static readonly Regex FolderKey = new(@"\A[A-Za-z0-9_-]{22}(?<child>/(?:file|folder)/[A-Za-z0-9_-]{8})?\z");

    private readonly string _handle;
    private readonly bool _folder;
    private readonly bool _child;
    public string? DownloadUrl { get; }

    private MegaPublicShare(string handle, bool folder, bool child, string? downloadUrl)
    {
        _handle = handle; _folder = folder; _child = child; DownloadUrl = downloadUrl;
    }

    public static bool IsHost(string host) => host is "mega.nz" or "mega.co.nz" or "www.mega.nz";

    public static MegaPublicShare? Parse(string url, string? accessCode = null)
    {
        // The download URL can enter the CLI's text protocol; check raw characters before URI normalization.
        if (string.IsNullOrWhiteSpace(url) || url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '\'' or '"' or '\\') ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || !IsHost(uri.IdnHost)) return null;
        if (uri.AbsolutePath == "/")
        {
            var legacy = Legacy.Match(uri.Fragment);
            if (!legacy.Success) return null;
            var folder = legacy.Groups["folder"].Success;
            var valid = (folder ? FolderKey : FileKey).IsMatch(legacy.Groups["key"].Value);
            return valid ? new(legacy.Groups["handle"].Value, folder, false, url) : null;
        }
        var link = Modern.Match(uri.AbsolutePath);
        if (!link.Success) return null;
        var isFolder = link.Groups["kind"].Value == "folder";
        var explicitFragment = url.Contains('#');
        var key = explicitFragment ? url[(url.IndexOf('#') + 1)..] : accessCode ?? "";
        var match = (isFolder ? FolderKey : FileKey).Match(key);
        // A malformed/unsupported selector must not be validated as the root.
        if (key.Contains('/') && (!match.Success || !explicitFragment)) return null;
        return new(link.Groups["handle"].Value, isFolder, isFolder && match.Groups["child"].Success,
            match.Success ? (explicitFragment ? url : url + "#" + key) : null);
    }

    public async Task<(string Status, string Reason)> CheckAsync(HttpClient client, CancellationToken ct)
    {
        // MEGA SDK CommandGetFile (no download URL flag) and CommandFolderLinkInfo:
        // https://github.com/meganz/sdk/blob/2c95a487b4b297dba1d792c88189d432c3452694/src/commands.cpp
        using var request = new HttpRequestMessage(HttpMethod.Post, MetadataEndpoint)
        {
            Content = _folder ? JsonContent.Create(new[] {new {a = "pli", ph = _handle}}) :
                JsonContent.Create(new[] {new {a = "g", p = _handle}})
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) return Access;
        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500) return Temporary;
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaximumMetadataBytes) return Unknown;
        try
        {
            await response.Content.LoadIntoBufferAsync(MaximumMetadataBytes, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1) return Unknown;
            var result = doc.RootElement[0];
            if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out var code))
                return code switch {-9 => (SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND"),
                    -11 or -16 or -14 => Access, -3 or -4 or -6 or -17 or -18 or -19 => Temporary, _ => Unknown};
            if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("e", out _)) return Unknown;
            if (result.TryGetProperty("d", out _)) return Access;
            if (_folder)
            {
                if (Text(result, "ph") != _handle || string.IsNullOrEmpty(Text(result, "attrs")) ||
                    Text(result, "k") is not {Length: > 9} key || !key.Contains(':') ||
                    !result.TryGetProperty("s", out var stats) || stats.ValueKind != JsonValueKind.Array ||
                    stats.GetArrayLength() != 5 || !stats.EnumerateArray().All(Nonnegative)) return Unknown;
                if (_child) return Unknown; // root metadata cannot establish that this specific child still exists
            }
            else if (string.IsNullOrEmpty(Text(result, "at")) || !result.TryGetProperty("s", out var size) || !Nonnegative(size))
                return Unknown;
            return DownloadUrl == null ? Access : (SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE");
        }
        catch (JsonException) { return Unknown; }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded) { return Unknown; }
    }

    private static string? Text(JsonElement obj, string name) => obj.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Nonnegative(JsonElement value) => value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) && number >= 0;
    private static (string,string) Unknown => (SourceAvailabilityStatuses.Unknown, "SOURCE_AVAILABILITY_UNDETERMINED");
    private static (string,string) Temporary => (SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT");
    private static (string,string) Access => (SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
}
