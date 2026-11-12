using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

internal interface IRcloneDriveDiscovery
{
    Task<RcloneDriveChoicesViewModel> DescribeAsync(string questionId, JsonElement profile,
        IReadOnlyList<RcloneConfigurationExampleViewModel> examples, CancellationToken ct);
    Task<RcloneDrivePreviewViewModel> PreviewAsync(string questionId, string driveId, JsonElement profile, CancellationToken ct);
}

/// <summary>Fixed-endpoint, read-only metadata using the current native staging token.
/// Refresh and all configuration changes remain in rclone. No URL from Graph is followed.</summary>
public sealed class RcloneDriveDiscovery : IRcloneDriveDiscovery
{
    private readonly Func<HttpClient> _client;
    public RcloneDriveDiscovery(BakabaseWebProxy proxy) => _client = () => new(CreateHandler(proxy)) {Timeout = Timeout.InfiniteTimeSpan};
    internal static SocketsHttpHandler CreateHandler(BakabaseWebProxy proxy) => new()
    {
        Proxy = proxy.ForDownloadTool(DownloadToolProxyTargets.Rclone, ThirdPartyId.OneDrive),
        UseProxy = true, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10)
    };
    internal RcloneDriveDiscovery(HttpMessageHandler handler) => _client = () => new(handler, false) {Timeout = Timeout.InfiniteTimeSpan};

    public async Task<RcloneDriveChoicesViewModel> DescribeAsync(string questionId, JsonElement profile,
        IReadOnlyList<RcloneConfigurationExampleViewModel> examples, CancellationToken ct)
    {
        var candidates = Candidates(examples).ToList();
        var result = new RcloneDriveChoicesViewModel {QuestionId = questionId, Candidates = candidates};
        try
        {
            using var client = _client();
            var token = AccessToken(profile);
            using var me = await Get(client, token, "me/drive?$select=id,name,driveType", ct);
            var id = Text(me.RootElement, "id");
            if (string.IsNullOrEmpty(id)) throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
            var index = candidates.FindIndex(c => c.DriveId == id);
            if (index < 0) return result with {Problem = "RCLONE_DRIVE_PRIMARY_NOT_LISTED"};
            try
            {
                using var root = await Get(client, token, Root(id!) + "?$select=id,name,folder,parentReference", ct);
                RequireRoot(root.RootElement);
                candidates[index] = candidates[index] with {IsPrimary = true, AccessStatus = "accessible",
                    Name = DisplayName(me.RootElement) ?? candidates[index].Name, DriveType = DriveType(me.RootElement)};
                return result;
            }
            catch (RcloneRemoteException error)
            {
                candidates[index] = candidates[index] with {AccessStatus = "unavailable", Problem = error.Code};
                return result with {Problem = error.Code};
            }
        }
        catch (RcloneRemoteException error) { return result with {Problem = error.Code}; }
    }

    internal static IReadOnlyList<RcloneDriveCandidateViewModel> Candidates(IEnumerable<RcloneConfigurationExampleViewModel> examples) =>
        examples.GroupBy(e => e.Value, StringComparer.Ordinal).Select(group =>
        {
            // Prefer a supplied display label over a label repeating the exact ID. This
            // changes presentation only; identity and primary verification never use names.
            var label = group.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Help) &&
                !e.Help.Contains(e.Value, StringComparison.Ordinal)) ?? group.First();
            return new RcloneDriveCandidateViewModel {DriveId = group.Key, Name = label.Help};
        }).ToList();

    public async Task<RcloneDrivePreviewViewModel> PreviewAsync(string questionId, string driveId, JsonElement profile, CancellationToken ct)
    {
        using var client = _client();
        var token = AccessToken(profile);
        using var root = await Get(client, token, Root(driveId) + "?$select=id,name,folder,parentReference", ct);
        RequireRoot(root.RootElement);
        using var children = await Get(client, token, Root(driveId) + "/children?$select=name,size,folder,file&$top=100", ct);
        if (!children.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 100)
            throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
        var items = new List<RcloneDrivePreviewItemViewModel>();
        foreach (var item in values.EnumerateArray())
        {
            var name = DisplayName(item) ?? throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
            var folder = item.TryGetProperty("folder", out var f) && f.ValueKind == JsonValueKind.Object;
            if (!folder && (!item.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.Object)) continue;
            long? size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var number) && number >= 0 ? number : null;
            items.Add(new(name, folder, size));
        }
        var parent = root.RootElement.TryGetProperty("parentReference", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        return new() {QuestionId = questionId, DriveId = driveId, Name = DisplayName(root.RootElement),
            DriveType = parent.ValueKind == JsonValueKind.Object ? DriveType(parent) : null, Items = items,
            HasMore = children.RootElement.TryGetProperty("@odata.nextLink", out var next) && next.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(next.GetString())};
    }

    private static string Root(string id) => "drives/" + Uri.EscapeDataString(id) + "/root";
    private static void RequireRoot(JsonElement root)
    {
        if (string.IsNullOrEmpty(Text(root, "id")) || !root.TryGetProperty("folder", out var folder) || folder.ValueKind != JsonValueKind.Object)
            throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
    }
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static string? DriveType(JsonElement value) => Text(value, "driveType") is "personal" or "business" or "documentLibrary" ? Text(value, "driveType") : null;
    private static string? DisplayName(JsonElement value) => Text(value, "name") is {Length: > 0 and <= 2048} name && !name.Any(char.IsControl) ? name : null;
    private static string AccessToken(JsonElement profile)
    {
        try
        {
            var json = Text(profile, "token");
            if (json == null || json.Length > 256 * 1024) throw new JsonException();
            using var token = JsonDocument.Parse(json);
            var access = Text(token.RootElement, "access_token");
            if (string.IsNullOrEmpty(access) || access.Length > 65536 || access.Any(char.IsControl)) throw new JsonException();
            return access;
        }
        catch (JsonException) { throw new RcloneRemoteException("RCLONE_CONFIGURATION_AUTHENTICATION_FAILED"); }
    }
    private static async Task<JsonDocument> Get(HttpClient client, string token, string path, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new RcloneRemoteException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "RCLONE_CONFIGURATION_AUTHENTICATION_FAILED",
                HttpStatusCode.Forbidden => "RCLONE_CONFIGURATION_ACCESS_DENIED",
                HttpStatusCode.NotFound => "RCLONE_CONFIGURATION_NOT_FOUND",
                HttpStatusCode.BadRequest => "RCLONE_CONFIGURATION_REQUEST_INVALID",
                HttpStatusCode.TooManyRequests => "RCLONE_CONFIGURATION_RATE_LIMITED",
                _ => "RCLONE_CONFIGURATION_SERVICE_UNAVAILABLE"
            });
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) != 0)
            {
                if (body.Length + read > 256 * 1024) throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
                body.Write(buffer, 0, read);
            }
            var document = JsonDocument.Parse(body.ToArray());
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose(); throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { throw new RcloneRemoteException("RCLONE_CONFIGURATION_NETWORK_FAILED"); }
        catch (JsonException) { throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID"); }
    }
}
