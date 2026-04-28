using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Components.Observation;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bakabase.Modules.AI.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/ai")]
public class AIController(
    ILlmProviderService providerService,
    ILlmUsageService usageService,
    ILlmCacheService cacheService,
    IAiFeatureService featureService,
    IAiTranslationService translationService,
    IAiFileProcessorService fileProcessorService,
    IResourceService resourceService
) : Controller
{
    // === Provider Management ===

    [HttpPost("providers")]
    [SwaggerOperation(OperationId = "AddLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel>> AddProvider(
        [FromBody] LlmProviderConfigAddInputModel model, CancellationToken ct)
    {
        return new(await providerService.AddProviderAsync(model, ct));
    }

    [HttpGet("providers")]
    [SwaggerOperation(OperationId = "GetAllLlmProviders")]
    public async Task<ListResponse<LlmProviderConfigDbModel>> GetAllProviders(CancellationToken ct)
    {
        return new(await providerService.GetAllProvidersAsync(ct));
    }

    [HttpGet("providers/{id:int}")]
    [SwaggerOperation(OperationId = "GetLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel?>> GetProvider(int id, CancellationToken ct)
    {
        return new(await providerService.GetProviderAsync(id, ct));
    }

    [HttpPut("providers/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel>> UpdateProvider(
        int id, [FromBody] LlmProviderConfigUpdateInputModel model, CancellationToken ct)
    {
        return new(await providerService.UpdateProviderAsync(id, model, ct));
    }

    [HttpDelete("providers/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteLlmProvider")]
    public async Task<BaseResponse> DeleteProvider(int id, CancellationToken ct)
    {
        await providerService.DeleteProviderAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("providers/{id:int}/test")]
    [SwaggerOperation(OperationId = "TestLlmProvider")]
    public async Task<SingletonResponse<bool>> TestProvider(int id, CancellationToken ct)
    {
        return new(await providerService.TestConnectionAsync(id, ct));
    }

    [HttpGet("providers/{id:int}/models")]
    [SwaggerOperation(OperationId = "GetLlmProviderModels")]
    public async Task<ListResponse<LlmModelInfo>> GetProviderModels(int id, CancellationToken ct)
    {
        return new(await providerService.GetModelsAsync(id, ct));
    }

    [HttpGet("provider-types")]
    [SwaggerOperation(OperationId = "GetLlmProviderTypes")]
    public ListResponse<LlmProviderTypeInfo> GetProviderTypes()
    {
        return new(providerService.GetProviderTypes());
    }

    // === Usage & Audit ===

    [HttpGet("usage")]
    [SwaggerOperation(OperationId = "SearchLlmUsage")]
    public async Task<ListResponse<LlmUsageLogDbModel>> SearchUsage(
        [FromQuery] int? providerConfigId, [FromQuery] string? modelId, [FromQuery] string? feature,
        [FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime,
        [FromQuery] int pageIndex = 0, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var criteria = new LlmUsageSearchCriteria
        {
            ProviderConfigId = providerConfigId,
            ModelId = modelId,
            Feature = feature,
            StartTime = startTime,
            EndTime = endTime,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
        return new(await usageService.SearchAsync(criteria, ct));
    }

    [HttpGet("usage/summary")]
    [SwaggerOperation(OperationId = "GetLlmUsageSummary")]
    public async Task<SingletonResponse<LlmUsageSummary>> GetUsageSummary(CancellationToken ct)
    {
        return new(await usageService.GetSummaryAsync(ct));
    }

    // === Cache ===

    [HttpGet("cache")]
    [SwaggerOperation(OperationId = "GetAllLlmCacheEntries")]
    public async Task<ListResponse<LlmCallCacheEntryDbModel>> GetAllCacheEntries(CancellationToken ct)
    {
        return new(await cacheService.GetAllAsync(ct));
    }

    [HttpDelete("cache/{id:long}")]
    [SwaggerOperation(OperationId = "DeleteLlmCacheEntry")]
    public async Task<BaseResponse> DeleteCacheEntry(long id, CancellationToken ct)
    {
        await cacheService.DeleteAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("cache")]
    [SwaggerOperation(OperationId = "ClearAllLlmCache")]
    public async Task<BaseResponse> ClearAllCache(CancellationToken ct)
    {
        await cacheService.ClearAllAsync(ct);
        return BaseResponseBuilder.Ok;
    }

    // === Feature Configs ===

    [HttpGet("features")]
    [SwaggerOperation(OperationId = "GetAllAiFeatureConfigs")]
    public async Task<ListResponse<AiFeatureConfigDbModel>> GetAllFeatureConfigs(CancellationToken ct)
    {
        return new(await featureService.GetAllConfigsAsync(ct));
    }

    [HttpGet("features/{feature}")]
    [SwaggerOperation(OperationId = "GetAiFeatureConfig")]
    public async Task<SingletonResponse<AiFeatureConfigDbModel?>> GetFeatureConfig(AiFeature feature,
        CancellationToken ct)
    {
        return new(await featureService.GetConfigAsync(feature, ct));
    }

    [HttpPut("features/{feature}")]
    [SwaggerOperation(OperationId = "SaveAiFeatureConfig")]
    public async Task<SingletonResponse<AiFeatureConfigDbModel>> SaveFeatureConfig(
        AiFeature feature, [FromBody] AiFeatureConfigInputModel model, CancellationToken ct)
    {
        var config = new AiFeatureConfigDbModel
        {
            Feature = feature,
            UseDefault = model.UseDefault,
            ProviderConfigId = model.ProviderConfigId,
            ModelId = model.ModelId,
            Temperature = model.Temperature,
            MaxTokens = model.MaxTokens,
            TopP = model.TopP
        };
        return new(await featureService.SaveConfigAsync(config, ct));
    }

    [HttpDelete("features/{feature}")]
    [SwaggerOperation(OperationId = "DeleteAiFeatureConfig")]
    public async Task<BaseResponse> DeleteFeatureConfig(AiFeature feature, CancellationToken ct)
    {
        await featureService.DeleteConfigAsync(feature, ct);
        return BaseResponseBuilder.Ok;
    }

    // === Translation ===

    [HttpPost("translate")]
    [SwaggerOperation(OperationId = "AiTranslate")]
    public async Task<SingletonResponse<TranslationResult>> Translate(
        [FromBody] TranslateInputModel model, CancellationToken ct)
    {
        return new(await translationService.TranslateAsync(model.Text, model.TargetLanguage, model.SourceLanguage, ct));
    }

    [HttpPost("translate/batch")]
    [SwaggerOperation(OperationId = "AiTranslateBatch")]
    public async Task<SingletonResponse<BatchTranslationResult>> TranslateBatch(
        [FromBody] TranslateBatchInputModel model, CancellationToken ct)
    {
        return new(await translationService.TranslateBatchAsync(model.Texts, model.TargetLanguage,
            model.SourceLanguage, ct));
    }

    // === Resource Translation ===

    [HttpPost("resource/{resourceId:int}/translate")]
    [SwaggerOperation(OperationId = "AiTranslateResourceProperties")]
    public async Task<SingletonResponse<ResourceTranslationResult>> TranslateResourceProperties(
        int resourceId, [FromBody] TranslateResourcePropertiesInputModel model, CancellationToken ct)
    {
        var resources = await resourceService.GetByKeys([resourceId], ResourceAdditionalItem.All);
        var resource = resources.FirstOrDefault();
        if (resource == null)
            return new SingletonResponse<ResourceTranslationResult>(
                new ResourceTranslationResult { ResourceId = resourceId })
            {
                Code = 404,
                Message = $"Resource {resourceId} not found"
            };

        var textsToTranslate =
            new List<(string Key, string Text, int PropId, PropertyPool Pool, Resource.Property PropMeta)>();

        if (resource.Properties != null)
        {
            foreach (var (pool, properties) in resource.Properties)
            {
                foreach (var (propId, propValue) in properties)
                {
                    if (propValue.Values == null) continue;
                    foreach (var val in propValue.Values)
                    {
                        if (val.BizValue is string strVal && !string.IsNullOrWhiteSpace(strVal))
                        {
                            textsToTranslate.Add((
                                $"{pool}:{propId}:{val.Scope}", strVal,
                                propId, (PropertyPool)pool, propValue));
                        }
                    }
                }
            }
        }

        if (textsToTranslate.Count == 0)
            return new(new ResourceTranslationResult
            {
                ResourceId = resourceId,
                Translations = []
            });

        var batchResult = await translationService.TranslateBatchAsync(
            textsToTranslate.Select(t => t.Text).ToList(),
            model.TargetLanguage,
            model.SourceLanguage,
            ct);

        var translations = textsToTranslate.Select((t, i) => new PropertyTranslation
        {
            PropertyKey = t.Key,
            OriginalText = t.Text,
            TranslatedText = i < batchResult.Results.Count
                ? batchResult.Results[i].TranslatedText
                : t.Text,
            PropertyId = t.PropId,
            PropertyPool = t.Pool,
            PropertyName = t.PropMeta.Name,
            PropertyType = t.PropMeta.Type,
            DbValueType = t.PropMeta.DbValueType,
            BizValueType = t.PropMeta.BizValueType,
        }).ToList();

        return new(new ResourceTranslationResult
        {
            ResourceId = resourceId,
            Translations = translations
        });
    }

    // === File Processor ===

    [HttpPost("file-processor/analyze-structure")]
    [SwaggerOperation(OperationId = "AiAnalyzeFileStructure")]
    public async Task<SingletonResponse<FileStructureAnalysisResult>> AnalyzeFileStructure(
        [FromBody] FileProcessorDirectoryInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.AnalyzeFileStructureAsync(model.DirectoryPath, model.ReferencePaths, ct));
    }

    [HttpPost("file-processor/analyze-naming")]
    [SwaggerOperation(OperationId = "AiAnalyzeNamingConvention")]
    public async Task<SingletonResponse<NamingConventionAnalysisResult>> AnalyzeNamingConvention(
        [FromBody] FileProcessorPathsInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.AnalyzeNamingConventionAsync(model.FilePaths, model.WorkingDirectory, model.ReferencePaths, ct));
    }

    [HttpPost("file-processor/suggest-names")]
    [SwaggerOperation(OperationId = "AiSuggestFileNameCorrections")]
    public async Task<SingletonResponse<FileNameCorrectionResult>> SuggestFileNameCorrections(
        [FromBody] FileNameCorrectionInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.SuggestFileNameCorrectionsAsync(
            model.FilePaths, model.WorkingDirectory, model.TargetConvention, model.ReferencePaths, ct));
    }

    [HttpPost("file-processor/group-by-similarity")]
    [SwaggerOperation(OperationId = "AiGroupByPathSimilarity")]
    public async Task<SingletonResponse<PathSimilarityGroupResult>> GroupByPathSimilarity(
        [FromBody] PathSimilarityGroupInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.GroupByPathSimilarityAsync(
            model.FilePaths, model.WorkingDirectory, model.CustomGroupingLogic, ct));
    }

    [HttpPost("file-processor/suggest-directory-corrections")]
    [SwaggerOperation(OperationId = "AiSuggestDirectoryCorrections")]
    public async Task<SingletonResponse<DirectoryStructureCorrectionResult>> SuggestDirectoryCorrections(
        [FromBody] FileProcessorDirectoryInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.SuggestDirectoryCorrectionsAsync(model.DirectoryPath, model.ReferencePaths, ct));
    }

    [HttpPost("file-processor/apply-operations")]
    [SwaggerOperation(OperationId = "AiApplyFileOperations")]
    public async Task<SingletonResponse<ApplyOperationsResult>> ApplyFileOperations(
        [FromBody] ApplyFileOperationsInputModel model, CancellationToken ct)
    {
        return new(await fileProcessorService.ApplyOperationsAsync(model.Operations, ct));
    }
}
