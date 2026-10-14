using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Search metadata/evaluation only: a library grant never reads or writes resource profile configuration.</summary>
[ApiController]
[Route("federation/v1/export/resource-profile")]
[FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.LibraryRead)]
public sealed class FederationResourceProfileExportController(ResourceProfileSourceReader reader,
    IResourceProfileSourceEvaluator evaluator, INodeIdentityProvider identity, IFederationQueryAccess access)
    : ControllerBase
{
    [HttpGet("identity")]
    [SwaggerOperation(OperationId = "GetFederationResourceProfileSourceInfo")]
    [ProducesResponseType(typeof(SingletonResponse<ResourceProfileSourceInfo>), 200)]
    public Task<IActionResult> Info(CancellationToken ct) => Read(async token =>
        new SingletonResponse<ResourceProfileSourceInfo>(await reader.InfoAsync(token)), ct);

    [HttpGet("properties")]
    [SwaggerOperation(OperationId = "GetFederationResourceProfileSearchProperties")]
    [ProducesResponseType(typeof(ListResponse<PropertyViewModel>), 200)]
    public Task<IActionResult> Properties(CancellationToken ct) => Read(reader.PropertiesAsync, ct);

    [HttpPost("filter-value")]
    [SwaggerOperation(OperationId = "GetFederationResourceProfileFilterValue")]
    [ProducesResponseType(typeof(SingletonResponse<PropertyViewModel>), 200)]
    public Task<IActionResult> FilterValue([FromBody] ResourceSearchFilterInputModel input, CancellationToken ct) =>
        Read(token => reader.FilterValueAsync(input, token), ct);

    [HttpPost("search-resolve")]
    [SwaggerOperation(OperationId = "ResolveFederationResourceProfileSearch")]
    [ProducesResponseType(typeof(SingletonResponse<ResourceSearchViewModel>), 200)]
    public Task<IActionResult> ResolveSearch([FromBody] ResourceSearchInputModel input, CancellationToken ct) =>
        Read(token => reader.ResolveSearchAsync(input, token), ct);

    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewFederationResourceProfileSearch")]
    [ProducesResponseType(typeof(SearchResponse<ResourceProfilePreviewResource>), 200)]
    public Task<IActionResult> Preview([FromBody] ResourceSearchInputModel input, CancellationToken ct) =>
        Read(token => reader.PreviewAsync(input, false, token), ct);

    [HttpPost("evaluate")]
    [SwaggerOperation(OperationId = "EvaluateFederationResourceProfiles")]
    [ProducesResponseType(typeof(SingletonResponse<ResourceProfileEvaluationResult>), 200)]
    public Task<IActionResult> Evaluate([FromBody] ResourceProfileEvaluationInput input, CancellationToken ct) =>
        Read(async token => new SingletonResponse<ResourceProfileEvaluationResult>(await evaluator.EvaluateAsync(input, token)), ct);

    private async Task<IActionResult> Read<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        var grant = FederationHttpContext.GetNodePrincipal(HttpContext)?.GrantId ??
                    throw new FederationQueryException("NodeAuthenticationRequired", 401);
        var node = await identity.GetAsync(ct);
        var epoch = Request.Query["libraryEpoch"].ToString();
        await access.ValidateAsync(grant, string.IsNullOrEmpty(epoch) ? node.LibraryEpoch : epoch, ct);
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(ct, access.GetCancellationToken(grant));
        authorized.CancelAfter(TimeSpan.FromSeconds(8));
        T result;
        try { result = await read(authorized.Token); }
        catch (ArgumentException e) { throw new FederationQueryException("InvalidProfileSearch", 422, e.Message); }
        authorized.Token.ThrowIfCancellationRequested();
        await access.ValidateAsync(grant, node.LibraryEpoch, authorized.Token);
        ResourceProfileSourceReader.CheckBudget(result!);
        var json = JsonConvert.SerializeObject(result, new JsonSerializerSettings
            {ContractResolver = new CamelCasePropertyNamesContractResolver()});
        authorized.Token.ThrowIfCancellationRequested();
        return Content(json, "application/json");
    }
}
