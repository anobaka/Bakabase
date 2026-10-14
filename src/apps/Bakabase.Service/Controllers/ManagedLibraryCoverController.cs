using System;
using System.IO;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>The owner chooses a cover from this resource, never from a caller-supplied filename.</summary>
[ApiController]
[Route("management/library/resources/{id:int}")]
public sealed class ManagedResourceAssetsController(IResourceService resources, ICoverProviderService covers) : ControllerBase
{
    [HttpGet("covers")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourceCovers")]
    public async Task<IActionResult> Covers(int id, CancellationToken ct)
    {
        var resource = await resources.Get(id, ResourceAdditionalItem.Cover);
        if (resource == null) return NotFound(new {code = "ResourceNotFound"});
        var paths = await covers.ResolveCoversAsync(resource, ct);
        return Ok(new {code = 0, data = paths?.Take(ManagedLibraryOperations.MaximumPageSize).ToArray() ?? []});
    }

    [HttpGet("cover")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourceCover")]
    public async Task<IActionResult> Cover(int id, int index = 0, int? w = null, int? h = null, CancellationToken ct = default)
    {
        if (!ValidCoverRequest(id, index, w, h)) return BadRequest(new {code = "InvalidResourceCover"});
        var resource = await resources.Get(id, ResourceAdditionalItem.Cover);
        if (resource == null) return NotFound(new {code = "ResourceNotFound"});
        var paths = await covers.ResolveCoversAsync(resource, ct);
        var path = paths is {Count: > 0} ? paths.ElementAtOrDefault(index) : index == 0 ? resource.Path : null;
        if (string.IsNullOrEmpty(path)) return NotFound(new {code = "ResourceCoverNotFound"});
        var thumbnails = ActivatorUtilities.CreateInstance<ToolController>(HttpContext.RequestServices);
        thumbnails.ControllerContext = ControllerContext;
        return await thumbnails.GetThumbnail(path, w, h);
    }

    [HttpGet("asset")]
    [RemoteAccessible(PathParameters = [nameof(key)])]
    [SwaggerOperation(OperationId = "GetManagedResourceAsset")]
    public async Task<IActionResult> Asset(int id, string key, bool thumbnail = false,
        int? w = null, int? h = null)
    {
        if (!ValidCoverRequest(id, 0, w, h) || string.IsNullOrWhiteSpace(key) || key.Length > 4096)
            return BadRequest(new {code = "InvalidResourceAsset"});
        var resource = await resources.Get(id, ResourceAdditionalItem.Properties);
        if (resource == null) return NotFound(new {code = "ResourceNotFound"});
        try
        {
            if (!ManagedLibraryResourceFiles.CanReadAsset(resource, key))
                return Conflict(new {code = "ResourceFileChanged", message = "Choose a file belonging to this resource."});
            if (thumbnail)
            {
                var thumbnails = ActivatorUtilities.CreateInstance<ToolController>(HttpContext.RequestServices);
                thumbnails.ControllerContext = ControllerContext;
                return await thumbnails.GetThumbnail(key, w, h);
            }
            var files = ActivatorUtilities.CreateInstance<FileController>(HttpContext.RequestServices);
            files.ControllerContext = ControllerContext;
            return await files.Play(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return Conflict(new {code = "ResourceFilesUnavailable", message = "The resource's files could not be read."}); }
    }

    [HttpGet("previewer")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourcePreviewerItems")]
    public async Task<IActionResult> Previewer(int id, CancellationToken ct)
    {
        var resource = await resources.Get(id);
        if (resource == null) return NotFound(new {code = "ResourceNotFound"});
        var page = ManagedLibraryBrowserFiles.Read(resource, null, 0, ManagedLibraryOperations.MaximumPageSize, ct);
        var files = page.Items.Where(i => i.Key.InferMediaType() is MediaType.Image or MediaType.Video).ToArray();
        var ffmpeg = HttpContext.RequestServices.GetRequiredService<FfMpegService>();
        var videosReady = false;
        if (files.Any(i => i.Key.InferMediaType() == MediaType.Video))
        {
            try { await ffmpeg.EnsureReadyAsync(ct); videosReady = true; }
            catch (DependencyNotInstalledException) { /* The ordinary preview also keeps images without ffmpeg. */ }
        }
        var items = new List<Bakabase.InsideWorld.Models.Models.Aos.PreviewerItem>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var type = file.Key.InferMediaType();
            if (type == MediaType.Video && !videosReady) continue;
            items.Add(new Bakabase.InsideWorld.Models.Models.Aos.PreviewerItem
            {
                FilePath = file.Key, Type = type,
                Duration = type == MediaType.Video ? (int) Math.Ceiling(await ffmpeg.GetDuration(file.Key, ct)) : 1
            });
        }
        return Ok(new {code = 0, data = items});
    }

    [HttpGet("children")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourceChildren")]
    public async Task<IActionResult> Children(int id, string? root = null, bool showHiddenFiles = false,
        string? cursor = null, int pageSize = ManagedLibraryOperations.MaximumPageSize, CancellationToken ct = default)
    {
        var resource = await resources.Get(id);
        if (resource == null) return NotFound(new {code = "ResourceNotFound"});
        var path = string.IsNullOrWhiteSpace(root) ? resource.Path : root;
        try
        {
            if (!ManagedLibraryResourceFiles.IsWithin(resource, path))
                return Conflict(new {code = "ResourceFileChanged", message = "Choose a location belonging to this resource."});
            var storage = HttpContext.RequestServices.GetRequiredService<IUserStoragePolicy>();
            storage.EnsurePathAllowed(path!);
            var page = ManagedLibraryResourceFiles.ReadChildren(resource, path!, showHiddenFiles,
                cursor, pageSize, entry => storage.IsPathAllowed(entry), ct);
            return Ok(new {code = 0, data = page});
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Conflict(new {code = "ResourceFilesUnavailable", message = "The resource's files could not be read."});
        }
    }

    [NonAction]
    public static bool ValidCoverRequest(int id, int index, int? w, int? h) => id > 0 &&
        index is >= 0 and < ManagedLibraryOperations.MaximumPageSize &&
        (!w.HasValue || w.Value is >= 1 and <= 2048) && (!h.HasValue || h.Value is >= 1 and <= 2048);
}

[ApiController]
[Route("federation/local/management/{serverId}")]
[FederationEndpoint(FederationEndpointKind.Local)]
[FederationServerController.InvalidRequest]
public sealed class ManagedLibraryCoverController(ManagedLibraryService library, IManagedServerService managed) : FederationControllerBase
{
    [HttpGet("cover")]
    [SwaggerOperation(OperationId = "GetManagedLibraryCover")]
    public async Task Cover(string serverId, string libraryEpoch, int resourceId, int index = 0,
        int? w = null, int? h = null, CancellationToken ct = default)
    {
        if (!ManagedResourceAssetsController.ValidCoverRequest(resourceId, index, w, h))
            throw new FederationAccessException("InvalidResourceCover", 400, "Choose a resource cover and a size up to 2048 pixels.");
        await library.RequireSourceAsync(serverId, libraryEpoch, ct);
        var path = $"/management/library/resources/{resourceId}/cover?index={index}" +
                   (w.HasValue ? $"&w={w}" : "") + (h.HasValue ? $"&h={h}" : "") +
                   "&managedLibraryEpoch=" + Uri.EscapeDataString(libraryEpoch);
        await StreamAsync(serverId, path, null, ct);
    }

    [HttpGet("asset")]
    [SwaggerOperation(OperationId = "GetManagedLibraryAsset")]
    public async Task Asset(string serverId, string libraryEpoch, int resourceId, string key,
        bool thumbnail = false, int? w = null, int? h = null, CancellationToken ct = default)
    {
        if (!ManagedResourceAssetsController.ValidCoverRequest(resourceId, 0, w, h) ||
            string.IsNullOrWhiteSpace(key) || key.Length > 4096)
            throw new FederationAccessException("InvalidResourceAsset", 400, "Choose a resource file.");
        var range = Request.Headers.Range.ToString();
        if (!string.IsNullOrEmpty(range) && (range.Length > 128 ||
            !System.Net.Http.Headers.RangeHeaderValue.TryParse(range, out var parsed) || parsed.Unit != "bytes" ||
            parsed.Ranges.Count != 1 || parsed.Ranges.Any(r => r.From.HasValue && r.To.HasValue && r.From > r.To)))
            throw new FederationAccessException("InvalidMediaRange", 400, "Only one byte range is supported.");
        await library.RequireSourceAsync(serverId, libraryEpoch, ct);
        var path = $"/management/library/resources/{resourceId}/asset?key={Uri.EscapeDataString(key)}&thumbnail={thumbnail}" +
                   (w.HasValue ? $"&w={w}" : "") + (h.HasValue ? $"&h={h}" : "") +
                   "&managedLibraryEpoch=" + Uri.EscapeDataString(libraryEpoch);
        await StreamAsync(serverId, path, range, ct);
    }

    [HttpHead("asset")]
    [SwaggerOperation(OperationId = "InspectManagedLibraryAsset")]
    public Task InspectAsset(string serverId, string libraryEpoch, int resourceId, string key,
        bool thumbnail = false, int? w = null, int? h = null, CancellationToken ct = default) =>
        Asset(serverId, libraryEpoch, resourceId, key, thumbnail, w, h, ct);

    private async Task StreamAsync(string serverId, string path, string? range, CancellationToken ct)
    {
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
            await using var stream = await upstream.Content.ReadAsStreamAsync(token);
            await FederationMediaStream.CopyAsync(stream, Response.Body, TimeSpan.FromSeconds(30), token);
        }, ct);
    }

    [HttpHead("cover")]
    [SwaggerOperation(OperationId = "InspectManagedLibraryCover")]
    public Task Inspect(string serverId, string libraryEpoch, int resourceId, int index = 0,
        int? w = null, int? h = null, CancellationToken ct = default) => Cover(serverId, libraryEpoch, resourceId, index, w, h, ct);
}
