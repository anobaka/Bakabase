using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc;

/// <summary>
/// One landed file ready to become an Artifact (and possibly a Resource).
/// </summary>
public record AigcLandedFile
{
    public required int Ordinal { get; init; }
    public required string AbsolutePath { get; init; }
    public required string RelativePath { get; init; }
}

/// <summary>
/// Shared logic to turn freshly-written AIGC files into Artifacts + Resources.
/// Used by both the generation BTask (after a provider call) and the import API.
/// </summary>
public class AigcArtifactPipeline<TDbContext>(
    ResourceService<TDbContext, AigcArtifactDbModel, int> artifactOrm,
    ResourceService<TDbContext, AigcGeneratorPropertyPresetDbModel, int> presetOrm,
    IResourceService resourceService,
    IResourceSourceLinkService sourceLinkService,
    IFileManager fileManager,
    ILogger<AigcArtifactPipeline<TDbContext>> logger
) where TDbContext : DbContext
{
    public const string AigcSourceName = "AIGC";

    public async Task<List<AigcArtifactDbModel>> RegisterAsync(
        AigcGeneratorDbModel generator,
        AigcGenerationRunDbModel run,
        IReadOnlyList<AigcLandedFile> files,
        CancellationToken ct)
    {
        if (files.Count == 0) return [];

        var presets = await presetOrm.GetAll(p => p.GeneratorId == generator.Id);

        var resources = new List<ResourceDbModel>();
        if (generator.ResourceMode == AigcArtifactResourceMode.PerArtifact)
        {
            foreach (var f in files)
            {
                resources.Add(BuildResourceDbModel(f.AbsolutePath, isFile: true));
            }
        }
        else
        {
            // PerRun: one resource per run, pointing at the run directory
            var runDir = fileManager.GetAigcRunDir(generator.Id, run.Id);
            resources.Add(BuildResourceDbModel(runDir, isFile: false));
        }

        var savedResources = await resourceService.AddAll(resources);

        // Source links
        var sourceKey = generator.ResourceMode == AigcArtifactResourceMode.PerArtifact
            ? null
            : $"g{generator.Id}/r{run.Id}";

        var artifacts = new List<AigcArtifactDbModel>();
        if (generator.ResourceMode == AigcArtifactResourceMode.PerArtifact)
        {
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var resourceId = savedResources[i].Id;

                await sourceLinkService.Add(new ResourceSourceLink
                {
                    ResourceId = resourceId,
                    Source = ResourceSource.Aigc,
                    SourceKey = $"g{generator.Id}/r{run.Id}/{file.Ordinal}",
                    CreateDt = DateTime.Now
                });

                artifacts.Add(new AigcArtifactDbModel
                {
                    RunId = run.Id,
                    GeneratorId = generator.Id,
                    OrdinalInRun = file.Ordinal,
                    RelativePath = file.RelativePath,
                    ResourceId = resourceId,
                    CreatedAt = DateTime.Now
                });
            }
        }
        else
        {
            var runResourceId = savedResources[0].Id;
            await sourceLinkService.Add(new ResourceSourceLink
            {
                ResourceId = runResourceId,
                Source = ResourceSource.Aigc,
                SourceKey = sourceKey!,
                CreateDt = DateTime.Now
            });

            foreach (var file in files)
            {
                artifacts.Add(new AigcArtifactDbModel
                {
                    RunId = run.Id,
                    GeneratorId = generator.Id,
                    OrdinalInRun = file.Ordinal,
                    RelativePath = file.RelativePath,
                    ResourceId = runResourceId,
                    CreatedAt = DateTime.Now
                });
            }
        }

        var insertedArtifacts = new List<AigcArtifactDbModel>();
        foreach (var a in artifacts)
        {
            var inserted = await artifactOrm.Add(a);
            if (inserted.Data is not null) insertedArtifacts.Add(inserted.Data);
        }

        // Apply property presets to every distinct resource we created
        var distinctResourceIds = artifacts.Select(a => a.ResourceId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        foreach (var resourceId in distinctResourceIds)
        {
            foreach (var preset in presets)
            {
                if (preset.SerializedBizValue is null) continue;
                try
                {
                    await resourceService.PutPropertyValue(resourceId, new ResourcePropertyValuePutInputModel
                    {
                        PropertyId = preset.PropertyId,
                        IsCustomProperty = preset.Pool == PropertyPool.Custom,
                        Value = preset.SerializedBizValue,
                        IsBizValue = true
                    });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to apply preset {PropPool}:{PropId} to AIGC resource {ResourceId}",
                        preset.Pool, preset.PropertyId, resourceId);
                }
            }
        }

        return insertedArtifacts;
    }

    private static ResourceDbModel BuildResourceDbModel(string path, bool isFile)
    {
        var fileInfo = isFile ? (FileSystemInfo)new FileInfo(path) : new DirectoryInfo(path);
        var fileCreate = fileInfo.Exists ? fileInfo.CreationTime : DateTime.Now;
        var fileModify = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now;
        return new ResourceDbModel
        {
            Path = path,
            IsFile = isFile,
            FileCreateDt = fileCreate,
            FileModifyDt = fileModify,
            CreateDt = DateTime.Now,
            UpdateDt = DateTime.Now,
            Status = ResourceStatus.Active
        };
    }
}
