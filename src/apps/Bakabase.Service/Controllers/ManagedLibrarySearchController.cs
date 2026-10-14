using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("management/library")]
public sealed class ManagedLibrarySearchController(IResourceService resources, IPropertyService properties, ICustomPropertyService customProperties) : ControllerBase
{
    [HttpPost("custom-properties/{id:int}/conversion-preview")]
    [SwaggerOperation(OperationId = "PreviewManagedCustomPropertyConversion")]
    public async Task<IActionResult> PreviewConversion(int id, PropertyType targetType)
    {
        var preview = await customProperties.PreviewTypeConversion(id, targetType);
        preview.Changes = preview.Changes.Take(ManagedLibraryOperations.MaximumPageSize).ToList();
        return Ok(new {code = 0, data = preview});
    }

    [HttpGet("resource-paths")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "SearchManagedResourcePaths")]
    public async Task<IActionResult> Paths(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length > 4096)
            return BadRequest(new {code = "InvalidKeyword"});
        var property = await properties.GetProperty(PropertyPool.Internal, (int) InternalProperty.RootPath);
        var response = await resources.Search(new ResourceSearch
        {
            PageIndex = 1,
            PageSize = ManagedLibraryOperations.MaximumPageSize,
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters = [new ResourceSearchFilter {PropertyPool = property.Pool, PropertyId = property.Id,
                    Property = property, Operation = SearchOperation.Contains, DbValue = keyword}]
            }
        }, ResourceAdditionalItem.None);
        return Ok(new {code = 0, data = response.Data?.Select(r => new ResourcePathInfoViewModel(r.Id, r.Path!, r.FileName!)).ToArray() ?? []});
    }
}
