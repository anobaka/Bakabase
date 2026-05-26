using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.AI.Components.Aigc;
using Bakabase.Modules.AI.Components.Aigc.Invokers;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class AigcGeneratorService<TDbContext>(
    ResourceService<TDbContext, AigcGeneratorDbModel, int> generatorOrm,
    ResourceService<TDbContext, AigcGeneratorPropertyPresetDbModel, int> presetOrm,
    ResourceService<TDbContext, AigcGenerationRunDbModel, int> runOrm,
    ResourceService<TDbContext, AiProviderDbModel, int> providerOrm,
    AigcArtifactPipeline<TDbContext> pipeline,
    BTaskManager taskManager,
    IBakabaseLocalizer localizer,
    IFileManager fileManager,
    ILogger<AigcGeneratorService<TDbContext>> logger
) : IAigcGeneratorService where TDbContext : DbContext
{
    public async Task<IReadOnlyList<AigcGeneratorView>> GetAllAsync(CancellationToken ct = default)
    {
        var generators = await generatorOrm.GetAll();
        if (generators.Count == 0) return [];
        var ids = generators.Select(g => g.Id).ToList();
        var presets = await presetOrm.GetAll(p => ids.Contains(p.GeneratorId));
        var byGen = presets.GroupBy(p => p.GeneratorId).ToDictionary(g => g.Key, g => (IReadOnlyList<AigcGeneratorPropertyPresetDbModel>)g.ToList());
        return generators.Select(g => new AigcGeneratorView
        {
            Generator = g,
            PropertyPresets = byGen.GetValueOrDefault(g.Id, [])
        }).ToList();
    }

    public async Task<AigcGeneratorView?> GetAsync(int id, CancellationToken ct = default)
    {
        var generator = await generatorOrm.GetByKey(id);
        if (generator == null) return null;
        var presets = await presetOrm.GetAll(p => p.GeneratorId == id);
        return new AigcGeneratorView { Generator = generator, PropertyPresets = presets };
    }

    public async Task<AigcGeneratorView> AddAsync(AigcGeneratorAddInputModel input, CancellationToken ct = default)
    {
        var dbModel = new AigcGeneratorDbModel
        {
            Name = input.Name,
            ProviderId = input.ProviderId,
            MediaType = input.MediaType,
            PromptTemplate = input.PromptTemplate,
            NegativePromptTemplate = input.NegativePromptTemplate,
            ParametersJson = input.ParametersJson,
            FilenameTemplate = string.IsNullOrEmpty(input.FilenameTemplate) ? "{run}_{ordinal}_{timestamp}" : input.FilenameTemplate,
            ResourceMode = input.ResourceMode,
            AllowDeletion = input.AllowDeletion,
            IsEnabled = input.IsEnabled,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        var saved = (await generatorOrm.Add(dbModel)).Data!;

        if (input.PropertyPresets is { Count: > 0 })
        {
            await ReplacePresetsAsync(saved.Id, input.PropertyPresets);
        }
        return (await GetAsync(saved.Id, ct))!;
    }

    public async Task<AigcGeneratorView> UpdateAsync(int id, AigcGeneratorUpdateInputModel input,
        CancellationToken ct = default)
    {
        var existing = await generatorOrm.GetByKey(id) ?? throw new InvalidOperationException($"Aigc generator {id} not found");
        if (input.Name != null) existing.Name = input.Name;
        if (input.ProviderId.HasValue) existing.ProviderId = input.ProviderId.Value;
        if (input.MediaType.HasValue) existing.MediaType = input.MediaType.Value;
        if (input.PromptTemplate != null) existing.PromptTemplate = input.PromptTemplate;
        if (input.NegativePromptTemplate != null) existing.NegativePromptTemplate = input.NegativePromptTemplate;
        if (input.ParametersJson != null) existing.ParametersJson = input.ParametersJson;
        if (input.FilenameTemplate != null) existing.FilenameTemplate = input.FilenameTemplate;
        if (input.ResourceMode.HasValue) existing.ResourceMode = input.ResourceMode.Value;
        if (input.AllowDeletion.HasValue) existing.AllowDeletion = input.AllowDeletion.Value;
        if (input.IsEnabled.HasValue) existing.IsEnabled = input.IsEnabled.Value;
        existing.UpdatedAt = DateTime.Now;
        await generatorOrm.Update(existing);

        if (input.PropertyPresets != null)
        {
            await ReplacePresetsAsync(id, input.PropertyPresets);
        }

        return (await GetAsync(id, ct))!;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await presetOrm.RemoveAll(p => p.GeneratorId == id);
        await generatorOrm.RemoveByKey(id);
    }

    public async Task<int> TriggerRunAsync(int generatorId, AigcGenerationTriggerInputModel? input, CancellationToken ct = default)
    {
        var generator = await generatorOrm.GetByKey(generatorId)
                        ?? throw new InvalidOperationException($"Aigc generator {generatorId} not found");
        if (!generator.IsEnabled)
            throw new InvalidOperationException("Generator is disabled.");

        var prompt = input?.PromptOverride ?? generator.PromptTemplate;
        var negative = input?.NegativePromptOverride ?? generator.NegativePromptTemplate;

        var run = new AigcGenerationRunDbModel
        {
            GeneratorId = generatorId,
            Status = AigcGenerationStatus.Pending,
            Prompt = prompt,
            NegativePrompt = negative,
            CreatedAt = DateTime.Now
        };
        var saved = (await runOrm.Add(run)).Data!;
        var runId = saved.Id;

        var taskId = $"AigcGenerationRun:{runId}";

        var builder = BTaskBuilder.Create(taskId)
            .Named(() => $"{localizer.BTask_Name("AigcGeneration")} #{runId}")
            .Describe(() => generator.Name)
            .ConflictsWith($"AigcProvider:{generator.ProviderId}")
            .Persistent()
            .StartImmediately()
            .IgnoreIfExists()
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<IAigcRunExecutor>();
                await executor.ExecuteAsync(runId,
                    async (pct, msg, innerCt) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = pct;
                            t.Process = msg;
                        });
                    },
                    args.CancellationToken);
            });

        await taskManager.Enqueue(builder);
        return runId;
    }

    public async Task<int> ImportArtifactsAsync(int generatorId, AigcArtifactImportInputModel input, CancellationToken ct = default)
    {
        var generator = await generatorOrm.GetByKey(generatorId)
                        ?? throw new InvalidOperationException($"Aigc generator {generatorId} not found");
        if (input.SourceFilePaths.Count == 0)
            throw new InvalidOperationException("No files supplied for import.");

        var run = new AigcGenerationRunDbModel
        {
            GeneratorId = generatorId,
            Status = AigcGenerationStatus.Imported,
            Prompt = null,
            CreatedAt = DateTime.Now,
            CompletedAt = DateTime.Now
        };
        var saved = (await runOrm.Add(run)).Data!;
        var runId = saved.Id;

        var runDir = fileManager.GetAigcRunDir(generatorId, runId);
        Directory.CreateDirectory(runDir);

        var landed = new List<AigcLandedFile>();
        for (var i = 0; i < input.SourceFilePaths.Count; i++)
        {
            var src = input.SourceFilePaths[i];
            if (!File.Exists(src))
            {
                logger.LogWarning("AIGC import: source file {Src} does not exist; skipping", src);
                continue;
            }
            var fileName = Path.GetFileName(src);
            var dest = Path.Combine(runDir, fileName);
            // Resolve naming collisions with a numeric suffix
            var collisionIdx = 1;
            while (File.Exists(dest))
            {
                var ext = Path.GetExtension(fileName);
                var stem = Path.GetFileNameWithoutExtension(fileName);
                dest = Path.Combine(runDir, $"{stem}_{collisionIdx++}{ext}");
            }
            File.Move(src, dest);

            landed.Add(new AigcLandedFile
            {
                Ordinal = i,
                AbsolutePath = dest,
                RelativePath = fileManager.GetAigcArtifactRelativePath(generatorId, runId, Path.GetFileName(dest))
            });
        }

        await pipeline.RegisterAsync(generator, saved, landed, ct);

        return runId;
    }

    public async Task<AigcGeneratorComfyUIImportResult> ImportComfyUIWorkflowsAsync(
        AigcGeneratorComfyUIImportInputModel input, CancellationToken ct = default)
    {
        var provider = await providerOrm.GetByKey(input.ProviderId)
                       ?? throw new InvalidOperationException($"AI provider {input.ProviderId} not found");
        if (provider.Kind != AiProviderKind.ComfyUI)
            throw new InvalidOperationException($"Provider {provider.Name} is not a ComfyUI provider.");

        var files = EnumerateJsonFiles(input.Paths).ToList();

        // Dedup keys for this provider. Mixed pool: seeded from existing DB rows, augmented
        // as we create new generators within this import batch — so collisions are caught
        // both against history and within-batch with a single check.
        var existing = await generatorOrm.GetAll(g => g.ProviderId == provider.Id);
        var knownHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in existing)
        {
            knownNames.Add(g.Name);
            var hash = ExtractWorkflowHash(g.ParametersJson);
            if (!string.IsNullOrEmpty(hash)) knownHashes.Add(hash);
        }

        var items = new List<AigcGeneratorComfyUIImportItemResult>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            JsonNode? root;
            try
            {
                var text = await File.ReadAllTextAsync(file, ct);
                root = AigcInvokerHelpers.ParseLenient(text);
                if (root is null)
                {
                    items.Add(new AigcGeneratorComfyUIImportItemResult
                    {
                        Path = file,
                        Status = AigcGeneratorComfyUIImportStatus.SkippedInvalidJson,
                        Reason = "Failed to parse JSON"
                    });
                    continue;
                }
            }
            catch (Exception ex)
            {
                items.Add(new AigcGeneratorComfyUIImportItemResult
                {
                    Path = file,
                    Status = AigcGeneratorComfyUIImportStatus.Failed,
                    Reason = ex.Message
                });
                continue;
            }

            var workflow = ExtractComfyUIWorkflow(root);
            if (workflow is null)
            {
                items.Add(new AigcGeneratorComfyUIImportItemResult
                {
                    Path = file,
                    Status = AigcGeneratorComfyUIImportStatus.SkippedNotComfyUIWorkflow,
                    Reason = "JSON is not a ComfyUI API-format workflow (no node with class_type found)"
                });
                continue;
            }

            var canonical = workflow.ToJsonString();
            var hash = ComputeSha256(canonical);

            var baseName = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "ComfyUI Workflow";

            var nameDup = knownNames.Contains(baseName);
            var contentDup = knownHashes.Contains(hash);
            if (nameDup || contentDup)
            {
                items.Add(new AigcGeneratorComfyUIImportItemResult
                {
                    Path = file,
                    Status = AigcGeneratorComfyUIImportStatus.SkippedDuplicate,
                    Reason = (nameDup, contentDup) switch
                    {
                        (true, true) => $"A configuration named '{baseName}' with identical workflow content already exists",
                        (true, false) => $"A configuration named '{baseName}' already exists",
                        _ => "A configuration with identical workflow content already exists",
                    }
                });
                continue;
            }

            var parametersJson = JsonSerializer.Serialize(new
            {
                workflow = workflow,
                workflowHash = hash,
            });

            try
            {
                var added = (await generatorOrm.Add(new AigcGeneratorDbModel
                {
                    Name = baseName,
                    ProviderId = provider.Id,
                    MediaType = AigcMediaType.Image,
                    ParametersJson = parametersJson,
                    FilenameTemplate = "{run}_{ordinal}_{timestamp}",
                    ResourceMode = AigcArtifactResourceMode.PerArtifact,
                    AllowDeletion = true,
                    IsEnabled = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                })).Data!;
                knownHashes.Add(hash);
                knownNames.Add(baseName);
                items.Add(new AigcGeneratorComfyUIImportItemResult
                {
                    Path = file,
                    Status = AigcGeneratorComfyUIImportStatus.Imported,
                    GeneratorId = added.Id
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create AigcGenerator from {File}", file);
                items.Add(new AigcGeneratorComfyUIImportItemResult
                {
                    Path = file,
                    Status = AigcGeneratorComfyUIImportStatus.Failed,
                    Reason = ex.Message
                });
            }
        }

        return new AigcGeneratorComfyUIImportResult
        {
            Items = items,
            ImportedCount = items.Count(i => i.Status == AigcGeneratorComfyUIImportStatus.Imported),
            SkippedCount = items.Count(i =>
                i.Status == AigcGeneratorComfyUIImportStatus.SkippedDuplicate ||
                i.Status == AigcGeneratorComfyUIImportStatus.SkippedInvalidJson ||
                i.Status == AigcGeneratorComfyUIImportStatus.SkippedNotComfyUIWorkflow),
            FailedCount = items.Count(i => i.Status == AigcGeneratorComfyUIImportStatus.Failed),
        };
    }

    private static IEnumerable<string> EnumerateJsonFiles(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (File.Exists(path))
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && seen.Add(path))
                    yield return path;
            }
            else if (Directory.Exists(path))
            {
                foreach (var f in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories))
                {
                    if (seen.Add(f)) yield return f;
                }
            }
        }
    }

    private static JsonObject? ExtractComfyUIWorkflow(JsonNode root)
    {
        // Bare API-format workflow: any value is an object with class_type
        if (root is JsonObject obj &&
            obj.Any(kv => kv.Value is JsonObject n && n.ContainsKey("class_type")))
        {
            return obj;
        }

        // ComfyUI's "Save" (non-API) format wraps the prompt in {"prompt": {...}} with a node graph;
        // we accept that shape only if its inner object looks like API-format.
        if (root is JsonObject wrapper && wrapper["prompt"] is JsonObject prompt &&
            prompt.Any(kv => kv.Value is JsonObject n && n.ContainsKey("class_type")))
        {
            // Detach from parent so it can be re-parented elsewhere.
            wrapper.Remove("prompt");
            return prompt;
        }

        return null;
    }

    private static string? ExtractWorkflowHash(string? parametersJson)
    {
        if (string.IsNullOrEmpty(parametersJson)) return null;
        var parsed = AigcInvokerHelpers.ParseLenient(parametersJson);
        return parsed?["workflowHash"]?.GetValue<string>();
    }

    private static string ComputeSha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task ReplacePresetsAsync(int generatorId, List<AigcGeneratorPropertyPresetInputModel> presets)
    {
        await presetOrm.RemoveAll(p => p.GeneratorId == generatorId);
        foreach (var p in presets)
        {
            await presetOrm.Add(new AigcGeneratorPropertyPresetDbModel
            {
                GeneratorId = generatorId,
                Pool = p.Pool,
                PropertyId = p.PropertyId,
                SerializedBizValue = p.SerializedBizValue
            });
        }
    }
}
