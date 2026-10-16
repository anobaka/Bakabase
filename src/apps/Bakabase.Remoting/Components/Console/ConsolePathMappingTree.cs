using System.Net;
using System.Text.Json;
using Bakabase.Remoting.Components.Connection;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// The selected owner's bounded resource-directory metadata, read through this relay's
/// signed, identity-checked connection. No controller filesystem is exposed to the page.
/// </summary>
public sealed class ConsolePathMappingTree(HttpClient http, IUpstreamTarget target)
{
    private const int MaximumPageSize = 200;
    private const int MaximumPathChars = 4096;
    private const int MaximumCursorChars = 12288;
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() {PropertyNameCaseInsensitive = true};

    public async Task<ConsolePathMappingTreeResponse> ReadAsync(string? parent, string? cursor, int limit,
        string? libraryEpoch, CancellationToken ct)
    {
        parent = Empty(parent);
        cursor = Empty(cursor);
        libraryEpoch = Empty(libraryEpoch);
        if (limit is < 1 or > MaximumPageSize || parent != null && !ValidPath(parent) ||
            cursor != null && !ValidCursor(cursor) || libraryEpoch?.Length > 128)
            return Failure(422, "InvalidMappingTreeRequest");
        if (!Uri.TryCreate(target.BaseAddress, UriKind.Absolute, out var root))
            return Failure(503, "ServerUnreachable");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            // Fixed owner-local operation: never /federation/local, and never a caller URL.
            var path = "/management/library/path-mapping-tree?limit=" + limit +
                       "&parent=" + Uri.EscapeDataString(parent ?? "") +
                       "&cursor=" + Uri.EscapeDataString(cursor ?? "");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, path));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                return Failure(502, "InvalidMappingTreeResponse");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bytes.Length + read > MaximumResponseBytes)
                    return Failure(502, "InvalidMappingTreeResponse");
                bytes.Write(buffer, 0, read);
            }
            bytes.Position = 0;
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Failure(409, "ManagementUpdateRequired");
                using var error = await JsonDocument.ParseAsync(bytes, cancellationToken: deadline.Token);
                var reason = error.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                    ? code.GetString() : null;
                return Failure((int)response.StatusCode, reason is "MappingTreeExpired" or "MappingTreeBusy" or
                    "MappingTreeMetadataTooLarge" or "InvalidMappingTreeRequest" or "LibraryEpochChanged"
                    ? reason : "MappingTreeUnavailable");
            }
            var page = await JsonSerializer.DeserializeAsync<ConsolePathMappingTreePage>(bytes, Json, deadline.Token);
            if (page == null || string.IsNullOrEmpty(page.LibraryEpoch) || page.LibraryEpoch.Length > 128 ||
                page.Nodes == null || page.Nodes.Length > limit || page.NextCursor != null && !ValidCursor(page.NextCursor) ||
                page.Nodes.Any(node => node == null || !ValidPath(node.ServerPath) || string.IsNullOrEmpty(node.Name) ||
                    node.Name.Length > MaximumPathChars || node.ResourceCount < 0) ||
                page.Nodes.Select(node => node.ServerPath).Distinct(StringComparer.Ordinal).Count() != page.Nodes.Length)
                return Failure(502, "InvalidMappingTreeResponse");
            if (libraryEpoch != null && page.LibraryEpoch != libraryEpoch)
                return Failure(409, "LibraryEpochChanged");
            return new(200, page, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        { return Failure(503, "MappingTreeUnavailable"); }
        catch (JsonException) { return Failure(502, "InvalidMappingTreeResponse"); }
    }

    private static ConsolePathMappingTreeResponse Failure(int status, string reason) => new(status, null, reason);
    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool ValidCursor(string cursor) => cursor.Length is > 0 and <= MaximumCursorChars &&
        cursor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool ValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && path.Length <= MaximumPathChars &&
        !path.Any(char.IsControl) && (path.StartsWith('/') || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\') &&
        !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..");
}

public sealed record ConsolePathMappingTreeNode(string ServerPath, string Name, long ResourceCount, bool HasChildren);
public sealed record ConsolePathMappingTreePage(string LibraryEpoch, ConsolePathMappingTreeNode[] Nodes, string? NextCursor);
public sealed record ConsolePathMappingTreeResponse(int StatusCode, ConsolePathMappingTreePage? Page, string? Reason);
