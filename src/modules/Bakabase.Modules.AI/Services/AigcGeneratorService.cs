using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.AI.Components.Aigc;
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

        var builder = new BTaskHandlerBuilder
        {
            Id = taskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => $"{localizer.BTask_Name("AigcGeneration")} #{runId}",
            GetDescription = () => generator.Name,
            ConflictKeys = [$"AigcProvider:{generator.ProviderId}"],
            Level = BTaskLevel.Default,
            IsPersistent = true,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
            Run = async args =>
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
            }
        };

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
