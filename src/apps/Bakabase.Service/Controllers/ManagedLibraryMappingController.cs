using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Only this owner's resource table is read; replicated resources and filesystem entries are never enumerated.</summary>
[ApiController]
[Route("management/library/path-mapping-tree")]
public sealed class ManagedLibraryMappingController(ManagedLibraryMappingTreeService tree) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetOwnedResourcePathMappingTree")]
    [ProducesResponseType(typeof(ManagedLibraryMappingTreePage), 200)]
    public async Task<IActionResult> Read([FromQuery] string? parent, [FromQuery] string? cursor,
        CancellationToken ct, [FromQuery] int limit = 200)
    {
        try { return Ok(await tree.ReadAsync(Empty(parent), Empty(cursor), limit, ct)); }
        catch (FederationQueryException error)
        {
            return StatusCode(error.StatusCode, new {code = error.Code, message = error.Message, retryable = error.Retryable});
        }
    }

    internal static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Uses an existing administrator pairing, never a library sharing grant.</summary>
[ApiController]
[Route("federation/local/servers")]
[FederationEndpoint(FederationEndpointKind.Local)]
[FederationServerController.InvalidRequest]
public sealed class ManagedServerMappingTreeController(ManagedLibraryService library) : FederationControllerBase
{
    [HttpGet("{serverId}/path-mapping-tree")]
    [SwaggerOperation(OperationId = "GetManagedServerPathMappingTree")]
    [ProducesResponseType(typeof(ManagedLibraryMappingTreePage), 200)]
    public async Task<IActionResult> Read(string serverId, [FromQuery] string? parent, [FromQuery] string? cursor,
        CancellationToken ct, [FromQuery] int limit = 200, [FromQuery] string? libraryEpoch = null)
    {
        parent = ManagedLibraryMappingController.Empty(parent);
        cursor = ManagedLibraryMappingController.Empty(cursor);
        ManagedLibraryMappingTreeService.ValidateRequest(parent, cursor, limit);
        if (!NodeRequestSignature.IsIdentifier(serverId))
            throw new FederationAccessException("InvalidServer", 400, "Choose a managed device.");
        var source = await library.RemoteInfoAsync(serverId, ct);
        if (libraryEpoch != null && source.LibraryEpoch != libraryEpoch)
            throw new FederationAccessException("LibraryEpochChanged", 409, "The device's library changed. Reopen its directory tree.");
        var query = new Dictionary<string, string[]> { ["limit"] = [limit.ToString(System.Globalization.CultureInfo.InvariantCulture)] };
        if (parent != null) query["parent"] = [parent];
        if (cursor != null) query["cursor"] = [cursor];
        var response = await library.ExecuteAsync(serverId,
            new ManagedLibraryRequest(source.LibraryEpoch, "resourcePathMappingTree", Query: query), ct);
        if (response.StatusCode == 404)
            throw new FederationAccessException("ManagementUpdateRequired", 409, "Update the managed device to browse its resource directories.");
        if (response.StatusCode is >= 200 and < 300)
        {
            ManagedLibraryMappingTreePage? page;
            try { page = JsonConvert.DeserializeObject<ManagedLibraryMappingTreePage>(response.Body); }
            catch (JsonException) { page = null; }
            if (page?.LibraryEpoch != source.LibraryEpoch || page.Nodes == null || page.Nodes.Length > limit)
                throw new FederationAccessException("InvalidMappingTreeResponse", 502, "The device returned an invalid directory page.");
        }
        return new ContentResult { StatusCode = response.StatusCode, ContentType = "application/json; charset=utf-8", Content = response.Body };
    }
}
