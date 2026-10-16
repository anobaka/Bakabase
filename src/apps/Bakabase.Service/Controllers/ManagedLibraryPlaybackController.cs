using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Federation;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ManagedLibraryPlayRequest(string LibraryEpoch, int ResourceId, DataOrigin Origin,
    string Key, bool ExplicitChoice = false);

public sealed record ManagedLibraryDirectoryRequest(string LibraryEpoch, int ResourceId);
public sealed record ManagedLibraryBatchRequest(string LibraryEpoch, int[] ResourceIds,
    string? PlayerKey = null, BatchPlayFileSelectionMode FileSelectionMode = BatchPlayFileSelectionMode.FirstFilePerResource);

[ApiController]
[Route("federation/local/management/{serverId}")]
[FederationEndpoint(FederationEndpointKind.Local)]
[FederationServerController.InvalidRequest]
public sealed class ManagedLibraryPlaybackController(ManagedLibraryService library, IManagedServerService managed)
    : FederationControllerBase
{
    [HttpPost("batch-play/candidates")]
    [RunsOnUserMachine(Reason = "The candidates depend on the players installed on this computer.")]
    [SwaggerOperation(OperationId = "GetManagedLibraryBatchPlayCandidates")]
    public Task<IActionResult> BatchCandidates(string serverId, [FromBody] ManagedLibraryBatchRequest request,
        [FromServices] IServerSelfDescription self, CancellationToken ct) => Batch(serverId, request, self, false, ct);

    [HttpPost("batch-play")]
    [RunsOnUserMachine(Reason = "The selected resources open together in a player on this computer.")]
    [SwaggerOperation(OperationId = "BatchPlayManagedLibraryResources")]
    public Task<IActionResult> BatchPlay(string serverId, [FromBody] ManagedLibraryBatchRequest request,
        [FromServices] IServerSelfDescription self, CancellationToken ct) => Batch(serverId, request, self, true, ct);

    private async Task<IActionResult> Batch(string serverId, ManagedLibraryBatchRequest request,
        IServerSelfDescription self, bool play, CancellationToken ct)
    {
        if (self.Kind != ServerKind.Desktop || !FederationAccessMiddleware.IsLocalCaller(HttpContext))
            throw new FederationAccessException("DesktopRequired", 403, "Use the Bakabase app on this computer to open a local player.");
        // Newtonsoft supplies zero for an omitted positional-record enum, including
        // the candidates endpoint whose ordinary request does not carry a file mode.
        var fileSelectionMode = request.FileSelectionMode == 0
            ? BatchPlayFileSelectionMode.FirstFilePerResource : request.FileSelectionMode;
        if (request.ResourceIds is not {Length: > 0 and <= ManagedLibraryOperations.MaximumPageSize} ||
            request.ResourceIds.Any(id => id <= 0) || !Enum.IsDefined(fileSelectionMode) ||
            play && (string.IsNullOrWhiteSpace(request.PlayerKey) || request.PlayerKey.Length > 4096))
            throw new FederationAccessException("InvalidResourceSelection", 400, "Choose at most 100 resources and an available player.");
        await library.RequireSourceAsync(serverId, request.LibraryEpoch, ct);
        try
        {
            // A playlist can intentionally repeat a resource; the shared batch service
            // reads distinct metadata but launches files in the supplied order.
            var response = await managed.BatchPlayManagedResourcesAsync(serverId, request.ResourceIds,
                request.LibraryEpoch, play ? request.PlayerKey : null, (int) fileSelectionMode, ct);
            return new ContentResult {StatusCode = response.StatusCode, ContentType = "application/json; charset=utf-8", Content = response.Body};
        }
        catch (NotSupportedException)
        { throw new FederationAccessException("DesktopRequired", 409, "Open this selection in the Bakabase desktop app to use a player on that computer."); }
        catch (InvalidOperationException)
        { throw new FederationAccessException("ManagedSourceChanged", 409, "The device connection changed. Refresh the selection before opening it."); }
    }

    [HttpPost("play")]
    [RunsOnUserMachine(Reason = "The player opens on the desktop running this app.")]
    [SwaggerOperation(OperationId = "PlayManagedLibraryResource")]
    public async Task<IActionResult> Play(string serverId, [FromBody] ManagedLibraryPlayRequest request,
        [FromServices] IServerSelfDescription self, CancellationToken ct)
    {
        // The federation-local filter authorizes management, not launching a program on a desktop.
        if (self.Kind != ServerKind.Desktop || !FederationAccessMiddleware.IsLocalCaller(HttpContext))
            throw new FederationAccessException("DesktopRequired", 403,
                "Use the Bakabase app on this computer to open a local player.");
        await ValidateCandidateAsync(serverId, request, ct);
        try
        {
            var response = await managed.PlayManagedResourceAsync(serverId, request.ResourceId, request.Origin,
                request.Key, request.LibraryEpoch, request.ExplicitChoice, ct);
            return new ContentResult {StatusCode = response.StatusCode, ContentType = "application/json; charset=utf-8",
                Content = response.Body};
        }
        catch (NotSupportedException)
        {
            throw new FederationAccessException("DesktopRequired", 409,
                "Open this resource in the Bakabase desktop app to use a player on that computer.");
        }
        catch (InvalidOperationException)
        {
            throw new FederationAccessException("ManagedSourceChanged", 409,
                "The device connection changed. Refresh the resource before opening it.");
        }
    }

    [HttpPost("directory")]
    [RunsOnUserMachine(Reason = "The folder opens in the file manager on this computer.")]
    [SwaggerOperation(OperationId = "OpenManagedLibraryResourceDirectory")]
    public async Task<IActionResult> Directory(string serverId, [FromBody] ManagedLibraryDirectoryRequest request,
        [FromServices] IServerSelfDescription self, CancellationToken ct)
    {
        if (self.Kind != ServerKind.Desktop || !FederationAccessMiddleware.IsLocalCaller(HttpContext))
            throw new FederationAccessException("DesktopRequired", 403, "Use the Bakabase app on this computer to open a local folder.");
        if (request.ResourceId <= 0) throw new FederationAccessException("InvalidResource", 400, "Choose a resource.");
        await library.RequireSourceAsync(serverId, request.LibraryEpoch, ct);
        try
        {
            var response = await managed.OpenManagedResourceDirectoryAsync(serverId, request.ResourceId, request.LibraryEpoch, ct);
            return new ContentResult {StatusCode = response.StatusCode, ContentType = "application/json; charset=utf-8", Content = response.Body};
        }
        catch (NotSupportedException)
        { throw new FederationAccessException("DesktopRequired", 409, "Open this resource in the Bakabase desktop app to use this computer's file manager."); }
        catch (InvalidOperationException)
        { throw new FederationAccessException("ManagedSourceChanged", 409, "The device connection changed. Refresh the resource before opening it."); }
    }

    [HttpGet("media")]
    [SwaggerOperation(OperationId = "StreamManagedLibraryMedia")]
    public async Task Media(string serverId, [FromQuery] string libraryEpoch, [FromQuery] int resourceId,
        [FromQuery] DataOrigin origin, [FromQuery] string key, CancellationToken ct)
    {
        var range = Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(range) && (range.Length > 128 || !RangeHeaderValue.TryParse(range, out var parsed) ||
                                            parsed.Unit != "bytes" || parsed.Ranges.Count != 1 ||
                                            parsed.Ranges.Any(r => r.From.HasValue && r.To.HasValue && r.From > r.To)))
            throw new FederationAccessException("InvalidMediaRange", 400, "Only one byte range is supported.");
        if (resourceId <= 0 || string.IsNullOrWhiteSpace(key) || key.Length > 4096)
            throw new FederationAccessException("InvalidPlayableItem", 400, "Choose a file from this resource.");
        await library.RequireSourceAsync(serverId, libraryEpoch, ct);
        if (origin is not (DataOrigin.FileSystem or DataOrigin.Manual) || !BrowserFile(key))
            throw new FederationAccessException("BrowserPlaybackUnavailable", 422, "This item needs a supported local application.");
        // The owner validates resource membership and playable-file rules on every read;
        // this is never a free filename proxy. Its action filter checks the epoch again.
        var path = $"/management/library/resources/{resourceId}/media?key=" + Uri.EscapeDataString(key) +
                   "&origin=" + (int) origin + "&managedLibraryEpoch=" + Uri.EscapeDataString(libraryEpoch);
        try
        {
            // The owner's browser endpoint supports GET only; a HEAD probe reads just its
            // headers and immediately disposes the upstream response without copying the body.
            await managed.SendMediaAsync(serverId, HttpMethod.Get, path, range, async (upstream, token) =>
            {
                Response.StatusCode = (int) upstream.StatusCode;
                Response.Headers.CacheControl = "no-store";
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
                Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                foreach (var header in new[] {"Content-Length", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified"})
                    if (upstream.Content.Headers.TryGetValues(header, out var values) || upstream.Headers.TryGetValues(header, out values))
                        Response.Headers[header] = string.Join(", ", values);
                if (HttpMethods.IsHead(Request.Method)) return;
                await using var body = await upstream.Content.ReadAsStreamAsync(token);
                await FederationMediaStream.CopyAsync(body, Response.Body, TimeSpan.FromSeconds(30), token);
            }, ct);
        }
        catch (InvalidOperationException)
        {
            throw new FederationAccessException("ManagedSourceUnavailable", 503,
                "The source connection changed. Refresh this resource before playing it again.");
        }
        catch (System.IO.IOException)
        {
            throw new FederationAccessException("ManagedSourceUnavailable", 503, "The source stopped sending this file. Reopen playback to retry.");
        }
        catch (ArgumentException)
        {
            throw new FederationAccessException("InvalidMediaRange", 400, "This media request is invalid.");
        }
    }

    [HttpHead("media")]
    [SwaggerOperation(OperationId = "InspectManagedLibraryMedia")]
    public Task Inspect(string serverId, [FromQuery] string libraryEpoch, [FromQuery] int resourceId,
        [FromQuery] DataOrigin origin, [FromQuery] string key, CancellationToken ct) =>
        Media(serverId, libraryEpoch, resourceId, origin, key, ct);

    private async Task ValidateCandidateAsync(string serverId, ManagedLibraryPlayRequest request, CancellationToken ct)
    {
        if (request.ResourceId <= 0 || string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 4096)
            throw new FederationAccessException("InvalidPlayableItem", 400, "Choose a playable file from this resource.");
        var response = await library.ExecuteAsync(serverId,
            new ManagedLibraryRequest(request.LibraryEpoch, "resourcePlayableItems", request.ResourceId), ct);
        if (response.StatusCode is < 200 or >= 300)
            throw new FederationAccessException("ManagedSourceUnavailable", 503, "The source could not read this resource's files.");
        ListResponse<PlayableItem>? candidates;
        try { candidates = JsonConvert.DeserializeObject<ListResponse<PlayableItem>>(response.Body); }
        catch (JsonException)
        {
            throw new FederationAccessException("ManagedSourceUnavailable", 503, "The source returned an invalid file list.");
        }
        if (candidates?.Code == 0 && candidates.Data != null)
        {
            if (candidates.Data.Any(i => i != null && i.Origin == request.Origin && i.Key == request.Key)) return;
            // An explicit selection in the web viewer may open a real file when no
            // playable-file rule exists. An explicit rule with zero matches stays closed.
            if (request.ExplicitChoice && request.Origin is DataOrigin.FileSystem or DataOrigin.Manual)
            {
                var fallback = await library.ExecuteAsync(serverId, new ManagedLibraryRequest(request.LibraryEpoch,
                    "resourceNativeFallback", request.ResourceId, Query: new()
                    {
                        ["key"] = [request.Key], ["origin"] = [((int) request.Origin).ToString()]
                    }), ct);
                try
                {
                    var allowed = JsonConvert.DeserializeObject<SingletonResponse<bool>>(fallback.Body);
                    if (fallback.StatusCode is >= 200 and < 300 && allowed?.Code == 0 && allowed.Data) return;
                }
                catch (JsonException)
                {
                    throw new FederationAccessException("ManagedSourceUnavailable", 503, "The source returned an invalid file validation response.");
                }
            }
        }
        throw new FederationAccessException("PlayableItemChanged", 409, "This file is no longer part of the resource's playable files. Refresh and choose again.");
    }

    private static bool BrowserFile(string key)
    {
        var extension = PlayerFileCapabilities.Extension(key);
        return InternalOptions.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
               InternalOptions.TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
