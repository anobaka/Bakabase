using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class ResourceSearchTool(IResourceService resourceService, IPropertyService propertyService) : ILlmTool
{
    [Description(
        "Search resources in the user's media library by keyword. Returns a list of matching resources with basic info (id, name, path). Use this when the user asks to find or search for resources.")]
    public async Task<string> SearchResources(
        [Description("Keyword to search for in resource names and paths")] string keyword,
        [Description("Page number (1-based), default 1")] int page = 1,
        [Description("Results per page, default 20, max 50")] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var inputModel = new ResourceSearchInputModel
        {
            Keyword = keyword,
            Page = page,
            PageSize = pageSize,
        };
        inputModel.StandardPageable();
        var domainModel = await inputModel.ToDomainModel(propertyService);
        var result = await resourceService.Search(domainModel, ResourceAdditionalItem.None);
        var items = result.Data?.Select(r => new
        {
            r.Id,
            r.FileName,
            r.DisplayName,
            r.Path,
            r.Directory,
        }).ToList();
        return JsonSerializer.Serialize(new
        {
            result.TotalCount,
            Page = page,
            PageSize = pageSize,
            Items = items,
        });
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SearchResources);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "SearchResources", Description = "Search resources by keyword", IsReadOnly = true };
    }
}
