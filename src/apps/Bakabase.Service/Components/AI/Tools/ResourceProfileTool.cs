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

/// <summary>
/// LLM tools for Resource Profile matching and effective configuration (read-only).
/// </summary>
public class ResourceProfileTool(IResourceService resourceService, IResourceProfileService resourceProfileService)
    : ILlmTool
{
    private const int MaxTemplatePreviewLength = 240;

    [Description(
        "Get Resource Profile data for a resource: all matching profiles (highest priority first) and the " +
        "aggregated effective configuration (name template, enhancer ids and target bindings, etc.). " +
        "Use this before running enhancers to see which enhancer IDs are attached for this resource.")]
    public async Task<string> GetResourceProfileContext(
        [Description("The resource ID")] int resourceId)
    {
        var resource = await resourceService.Get(resourceId, ResourceAdditionalItem.None);
        if (resource == null)
        {
            return JsonSerializer.Serialize(new { Error = $"Resource {resourceId} not found" });
        }

        var matching = await resourceProfileService.GetMatchingProfiles(resource);
        var effectiveEnhancers = await resourceProfileService.GetEffectiveEnhancerOptions(resource);
        var effectiveNameTemplate = await resourceProfileService.GetEffectiveNameTemplate(resource);
        var effectivePropertyOptions = await resourceProfileService.GetEffectivePropertyOptions(resource);
        var effectivePlayableFile = await resourceProfileService.GetEffectivePlayableFileOptions(resource);
        var effectivePlayer = await resourceProfileService.GetEffectivePlayerOptions(resource);

        var matchingProfiles = matching.Select(p => new
        {
            p.Id,
            p.Name,
            p.Priority,
            HasNameTemplate = !string.IsNullOrEmpty(p.NameTemplate),
            NameTemplatePreview = Truncate(p.NameTemplate, MaxTemplatePreviewLength),
            EnhancerIds = p.EnhancerOptions?.Enhancers?.Select(e => e.EnhancerId).ToList() ?? [],
            HasPlayableFileOptions = p.PlayableFileOptions != null,
            HasPlayerOptions = p.PlayerOptions != null,
            HasPropertyOptions = p.PropertyOptions?.Properties?.Count > 0,
            PropertyRefCount = p.PropertyOptions?.Properties?.Count ?? 0,
        }).ToList();

        var effectiveEnhancerSummaries = effectiveEnhancers.Select(e => new
        {
            e.EnhancerId,
            TargetOptionsCount = e.TargetOptions?.Count ?? 0,
            Targets = e.TargetOptions?
                .Where(t => t.PropertyId > 0)
                .Select(t => new
                {
                    t.Target,
                    t.PropertyId,
                    PropertyPool = (int)t.PropertyPool,
                    DynamicTarget = t.DynamicTarget,
                })
                .ToList(),
            KeywordProperty = e.KeywordProperty == null
                ? null
                : new { Pool = (int)e.KeywordProperty.Pool, e.KeywordProperty.Id, Scope = (int)e.KeywordProperty.Scope },
            e.PretreatKeyword,
            e.Requirements,
            ExpressionsCount = e.Expressions?.Count ?? 0,
            TranslationEnabled = e.TranslationOptions is { Enabled: true },
        }).ToList();

        var propertyRefs = effectivePropertyOptions?.Properties?
            .Select(p => new
            {
                Pool = (int)p.Pool,
                p.Id,
                ScopePriority = p.ScopePriority?.Select(s => (int)s).ToList(),
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            ResourceId = resourceId,
            MatchingProfiles = matchingProfiles,
            MatchingProfileCount = matchingProfiles.Count,
            Effective = new
            {
                NameTemplate = Truncate(effectiveNameTemplate, MaxTemplatePreviewLength),
                Enhancers = effectiveEnhancerSummaries,
                EffectiveEnhancerIds = effectiveEnhancerSummaries.Select(x => x.EnhancerId).ToList(),
                PropertyReferences = propertyRefs,
                HasPlayableFileOptions = effectivePlayableFile != null,
                HasPlayerOptions = effectivePlayer != null,
            },
        });
    }

    private static string? Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLen)
        {
            return s;
        }

        return s[..maxLen] + "…";
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetResourceProfileContext);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata
        {
            Name = "GetResourceProfileContext",
            Description = "Get matching Resource Profiles and effective enhancer/name/property options for a resource",
            IsReadOnly = true,
        };
    }
}
