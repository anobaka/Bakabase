using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ResourceScopeInput(string Name, ResourceSearchInputModel? Search,
    string? SourceDeviceId = null, string? SourceLibraryEpoch = null, int? ExpectedRevision = null);
public sealed record ResourceScopeView(int Id, string Name, ResourceSearchViewModel? Search,
    string? SourceDeviceId, string? SourceLibraryEpoch, int Revision, int RuleCount);
public sealed record ResourceScopeBindInput(int? ScopeId, DateTime ExpectedUpdatedAt);
public sealed record ResourceProfileRuleInput(ResourceProfileRuleKind Kind, string? ValueJson, DateTime ExpectedUpdatedAt);

/// <summary>Administrator-only configuration; resource ownership and existing rule precedence stay unchanged.</summary>
[ApiController]
[Route("resource-scope")]
public sealed class ResourceScopeController(IResourceScopeService scopes, IResourceProfileService profiles,
    IPropertyService properties, IPropertyLocalizer localizer) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetResourceScopes")]
    public async Task<IReadOnlyList<ResourceScopeView>> Get()
    {
        var counts = (await profiles.GetAll()).Where(p => p.ResourceScopeId != null)
            .GroupBy(p => p.ResourceScopeId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<ResourceScopeView>();
        foreach (var scope in await scopes.GetAll())
        {
            var profile = new ResourceProfile { SourceDeviceId = scope.SourceDeviceId, SearchJson = scope.SearchJson };
            if (scope.SourceDeviceId == null && scope.SearchJson != null)
                profile.Search = await (JsonConvert.DeserializeObject<ResourceSearchDbModel>(scope.SearchJson) ?? new())
                    .ToDomainModel(properties);
            result.Add(new(scope.Id, scope.Name, scope.SourceDeviceId == null ? profile.Search.ToViewModel(localizer) : profile.ToViewModel(localizer).Search, scope.SourceDeviceId,
                scope.SourceLibraryEpoch, scope.Revision, counts.GetValueOrDefault(scope.Id)));
        }
        return result.OrderBy(s => s.Name).ThenBy(s => s.Id).ToArray();
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddResourceScope")]
    public Task<IActionResult> Add([FromBody] ResourceScopeInput input) => Change(async () =>
        await scopes.Add(input.Name, Search(input.Search), input.SourceDeviceId, input.SourceLibraryEpoch));

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdateResourceScope")]
    public Task<IActionResult> Update(int id, [FromBody] ResourceScopeInput input) => Change(async () =>
    {
        if (input.ExpectedRevision is not > 0) throw new ArgumentException("Reload the scope before saving.");
        var current = (await scopes.GetAll()).SingleOrDefault(s => s.Id == id)
            ?? throw new ResourceScopeConflictException("The resource scope no longer exists.");
        if (input.SourceDeviceId != current.SourceDeviceId || input.SourceLibraryEpoch != current.SourceLibraryEpoch)
            throw new ArgumentException("A scope's source device cannot be changed. Create a separate scope for that device.");
        return await scopes.Update(id, input.ExpectedRevision.Value, input.Name, Search(input.Search));
    });

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteResourceScope")]
    public Task<IActionResult> Delete(int id, [FromQuery] int expectedRevision) => Change(async () =>
    {
        await scopes.Delete(id, expectedRevision);
        return new { deleted = true };
    });

    [HttpPatch("profile/{profileId:int}")]
    [SwaggerOperation(OperationId = "BindResourceProfileScope")]
    public Task<IActionResult> Bind(int profileId, [FromBody] ResourceScopeBindInput input) => Change(async () =>
        (await scopes.Bind(profileId, input.ScopeId, input.ExpectedUpdatedAt)).ToViewModel(localizer));

    [HttpPatch("profile/{profileId:int}/rule")]
    [SwaggerOperation(OperationId = "UpdateResourceProfileRule")]
    public Task<IActionResult> Rule(int profileId, [FromBody] ResourceProfileRuleInput input) => Change(async () =>
        (await scopes.UpdateRule(profileId, input.Kind, input.ValueJson, input.ExpectedUpdatedAt)).ToViewModel(localizer));

    private static string? Search(ResourceSearchInputModel? search) =>
        search == null ? null : JsonConvert.SerializeObject(search.ToDbModel());

    private async Task<IActionResult> Change(Func<Task<object>> operation)
    {
        try { return Ok(await operation()); }
        catch (ResourceScopeConflictException e) { return Conflict(new { code = "ResourceScopeChanged", message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { code = "InvalidResourceScope", message = e.Message }); }
    }
}
