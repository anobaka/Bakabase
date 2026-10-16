using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("management/library/resources/{id:int}")]
public sealed class ManagedLibraryBrowserFilesController(IResourceService resources, IResourceProfileService profiles)
    : ControllerBase
{
    [HttpGet("browser-files")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourceBrowserFiles")]
    public async Task<IActionResult> Files(int id, int offset = 0, int pageSize = 100, string scope = "configured",
        string? initialPath = null, CancellationToken ct = default)
    {
        try
        {
            if (scope is not ("configured" or "all")) throw new ArgumentException("Choose all or configured resource files.");
            var resource = await resources.Get(id);
            if (resource == null) return Missing();
            var options = await profiles.GetEffectivePlayableFileOptions(resource);
            var page = ManagedLibraryBrowserFiles.Read(resource, options, offset, pageSize, ct, scope == "all", initialPath);
            // Other owner providers may materialize files outside Resource.Path. Their exact
            // discovered candidates remain authoritative; never infer a new filesystem root.
            if (!resource.HasLocalPath)
            {
                var candidates = (await resources.DiscoverPlayableItems(id, ct))
                    .Where(i => i.Origin is DataOrigin.FileSystem or DataOrigin.Manual &&
                                ManagedLibraryBrowserFiles.BrowserFile(i.Key)).ToList();
                var selectedIndex = string.IsNullOrEmpty(initialPath) ? -1 : candidates.FindIndex(i => i.Key == initialPath);
                if (selectedIndex >= 0) offset = selectedIndex / pageSize * pageSize;
                var discovered = candidates.Skip(offset).Take(pageSize + 1).ToList();
                var more = discovered.Count > pageSize;
                page = new(discovered.Take(pageSize).ToArray(), offset, more ? offset + pageSize : null,
                    more, true);
            }
            return Ok(new {code = 0, data = page});
        }
        catch (ArgumentException e) { return BadRequest(new {code = "InvalidBrowserPage", message = e.Message}); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return Unavailable(); }
    }

    [HttpGet("media")]
    [RemoteAccessible(PathParameters = [nameof(key)])]
    [SwaggerOperation(OperationId = "PlayManagedResourceBrowserFile")]
    public async Task<IActionResult> Media(int id, string key, DataOrigin origin = DataOrigin.FileSystem)
    {
        try
        {
            if (origin is not (DataOrigin.FileSystem or DataOrigin.Manual) || string.IsNullOrWhiteSpace(key) || key.Length > 4096)
                return BadRequest(new {code = "InvalidBrowserFile", message = "Choose a file belonging to this resource."});
            var resource = await resources.Get(id);
            if (resource == null) return Missing();
            var options = await profiles.GetEffectivePlayableFileOptions(resource);
            if (!ManagedLibraryBrowserFiles.CanRead(resource, options, key) &&
                !(ManagedLibraryBrowserFiles.MayUseAlternateProvider(resource, key) &&
                  IsDiscoveredBrowserFile(await resources.DiscoverPlayableItems(id, HttpContext.RequestAborted), origin, key)))
                return Conflict(new {code = "PlayableItemChanged", message = "This file is not available in the resource's current playback rules."});
            // The same streaming implementation preserves ranges, text, remuxing and the host's transcode policy.
            var files = ActivatorUtilities.CreateInstance<FileController>(HttpContext.RequestServices);
            files.ControllerContext = ControllerContext;
            return await files.Play(key);
        }
        catch (ArgumentException) { return BadRequest(new {code = "InvalidBrowserFile", message = "Choose a valid resource file."}); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return Unavailable(); }
    }

    [HttpGet("native-fallback")]
    [RemoteAccessible(PathParameters = [nameof(key)])]
    [SwaggerOperation(OperationId = "ValidateManagedResourceNativeFallback")]
    public async Task<IActionResult> NativeFallback(int id, string key, DataOrigin origin = DataOrigin.FileSystem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 4096)
                return BadRequest(new {code = "InvalidBrowserFile", message = "Choose a file belonging to this resource."});
            var resource = await resources.Get(id);
            if (resource == null) return Missing();
            var options = await profiles.GetEffectivePlayableFileOptions(resource);
            return Ok(new {code = 0, data = ManagedLibraryBrowserFiles.CanExplicitlyOpenUnconfiguredFile(resource, options, origin, key)});
        }
        catch (ArgumentException) { return BadRequest(new {code = "InvalidBrowserFile", message = "Choose a valid resource file."}); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { return Unavailable(); }
    }

    private IActionResult Missing() => NotFound(new {code = "ResourceNotFound", message = "The resource no longer exists."});
    private IActionResult Unavailable() => StatusCode(409,
        new {code = "ResourceFilesUnavailable", message = "The resource's files could not be read. Check its storage connection and permissions."});

    [NonAction]
    public static bool IsDiscoveredBrowserFile(System.Collections.Generic.IEnumerable<PlayableItem> candidates,
        DataOrigin origin, string key) => ManagedLibraryBrowserFiles.BrowserFile(key) &&
                                        candidates.Any(i => i.Origin == origin && i.Key == key);
}
