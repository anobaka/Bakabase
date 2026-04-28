using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.AI.Components.Tools;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class ResourceDetailTool(IResourceService resourceService) : ILlmTool
{
    [Description("Get detailed information about a specific resource by its ID, including all properties.")]
    public async Task<string> GetResourceDetail(
        [Description("The resource ID")] int resourceId)
    {
        var resource = await resourceService.Get(resourceId, ResourceAdditionalItem.All);
        if (resource == null)
            return JsonSerializer.Serialize(new { Error = $"Resource {resourceId} not found" });

        return JsonSerializer.Serialize(new
        {
            resource.Id,
            resource.FileName,
            resource.DisplayName,
            resource.Path,
            resource.Directory,
            resource.CreatedAt,
            resource.UpdatedAt,
            resource.FileCreatedAt,
            resource.FileModifiedAt,
            resource.PlayedAt,
            resource.Properties,
        });
    }

    [Description("Get details of multiple resources by their numeric database IDs in one call.")]
    public async Task<string> GetResourcesByIds(
        [Description("Array of resource IDs")] int[] resourceIds)
    {
        var resources = await resourceService.GetByKeys(resourceIds, ResourceAdditionalItem.All);
        var items = resources.Select(r => new
        {
            r.Id,
            r.FileName,
            r.DisplayName,
            r.Path,
            r.Properties,
        });
        return JsonSerializer.Serialize(items);
    }

    [Description(
        "Delete resources by their database IDs. Removes library records and related data. " +
        "If deleteFiles is true, also deletes files or directories on disk (irreversible). Only use when the user explicitly wants removal.")]
    public async Task<string> DeleteResourcesByIds(
        [Description("Resource IDs to delete")] int[] resourceIds,
        [Description("If true, also delete underlying files or folders from disk. Default false (safer).")] bool deleteFiles = false)
    {
        if (resourceIds == null || resourceIds.Length == 0)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = "No resource IDs provided" });
        }

        var distinct = resourceIds.Distinct().ToArray();
        const int maxBatch = 200;
        if (distinct.Length > maxBatch)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = $"Too many IDs (max {maxBatch} per call)" });
        }

        try
        {
            await resourceService.DeleteByKeys(distinct, deleteFiles);
            return JsonSerializer.Serialize(new
            {
                Success = true,
                DeletedCount = distinct.Length,
                DeletedIds = distinct,
                deleteFiles,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = ex.Message });
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetResourceDetail);
        yield return AIFunctionFactory.Create(GetResourcesByIds);
        yield return AIFunctionFactory.Create(DeleteResourcesByIds);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "GetResourceDetail", Description = "Get detailed resource information by ID", IsReadOnly = true };
        yield return new LlmToolMetadata { Name = "GetResourcesByIds", Description = "Get multiple resources by database IDs", IsReadOnly = true };
        yield return new LlmToolMetadata { Name = "DeleteResourcesByIds", Description = "Delete resources by database IDs (optional disk file removal)", IsReadOnly = false };
    }
}
