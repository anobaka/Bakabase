using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.AI.Components.Aigc;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class AigcArtifactService<TDbContext>(
    ResourceService<TDbContext, AigcGenerationRunDbModel, int> runOrm,
    ResourceService<TDbContext, AigcArtifactDbModel, int> artifactOrm,
    ResourceService<TDbContext, AigcGeneratorDbModel, int> generatorOrm,
    IResourceService resourceService,
    IFileManager fileManager,
    BTaskManager taskManager,
    ILogger<AigcArtifactService<TDbContext>> logger
) : IAigcArtifactService where TDbContext : DbContext
{
    public async Task<IReadOnlyList<AigcGenerationRunDbModel>> GetRunsAsync(int? generatorId, CancellationToken ct = default)
    {
        var runs = generatorId.HasValue
            ? await runOrm.GetAll(r => r.GeneratorId == generatorId.Value)
            : await runOrm.GetAll();
        return runs.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<AigcGenerationRunDbModel?> GetRunAsync(int runId, CancellationToken ct = default) =>
        await runOrm.GetByKey(runId);

    public async Task DeleteRunAsync(int runId, CancellationToken ct = default)
    {
        var artifacts = await artifactOrm.GetAll(a => a.RunId == runId);
        var resourceIds = artifacts.Where(a => a.ResourceId.HasValue).Select(a => a.ResourceId!.Value).Distinct().ToArray();

        if (resourceIds.Length > 0)
        {
            await resourceService.DeleteByKeys(resourceIds, deleteFiles: true);
        }

        // Best-effort: also nuke the run directory in case it has leftover files.
        var run = await runOrm.GetByKey(runId);
        if (run != null)
        {
            var runDir = fileManager.GetAigcRunDir(run.GeneratorId, run.Id);
            if (Directory.Exists(runDir))
            {
                try { Directory.Delete(runDir, recursive: true); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to clean AIGC run dir {Dir}", runDir); }
            }
        }

        await artifactOrm.RemoveAll(a => a.RunId == runId);
        await runOrm.RemoveByKey(runId);
    }

    public async Task StopRunAsync(int runId, CancellationToken ct = default)
    {
        var run = await runOrm.GetByKey(runId)
                  ?? throw new InvalidOperationException($"Run {runId} not found");

        // Already terminal — nothing to do.
        if (run.Status is AigcGenerationStatus.Succeeded
                       or AigcGenerationStatus.Failed
                       or AigcGenerationStatus.Imported
                       or AigcGenerationStatus.Cancelled)
        {
            return;
        }

        // Best-effort cancel: BTaskManager.Stop signals the CancellationToken on the running
        // task and (for pending tasks) removes them from the queue. Wrapped because Stop
        // throws if the id is unknown — that can legitimately happen if the daemon already
        // cleaned the task up, in which case we still want to flip the DB status below.
        var taskId = $"AigcGenerationRun:{runId}";
        try { await taskManager.Stop(taskId); }
        catch (Exception ex) { logger.LogWarning(ex, "BTask {Task} stop failed (may already be gone)", taskId); }

        run.Status = AigcGenerationStatus.Cancelled;
        run.CompletedAt = DateTime.Now;
        if (string.IsNullOrEmpty(run.ErrorMessage)) run.ErrorMessage = "Cancelled by user";
        await runOrm.Update(run);
    }

    public async Task<IReadOnlyList<AigcArtifactDbModel>> GetArtifactsAsync(int? generatorId, int? runId, CancellationToken ct = default)
    {
        if (runId.HasValue)
        {
            return (await artifactOrm.GetAll(a => a.RunId == runId.Value))
                .OrderBy(a => a.OrdinalInRun).ToList();
        }
        if (generatorId.HasValue)
        {
            return (await artifactOrm.GetAll(a => a.GeneratorId == generatorId.Value))
                .OrderByDescending(a => a.CreatedAt).ToList();
        }
        return (await artifactOrm.GetAll())
            .OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<string?> GetArtifactAbsolutePathAsync(int artifactId, CancellationToken ct = default)
    {
        var artifact = await artifactOrm.GetByKey(artifactId);
        return artifact == null ? null : Path.Combine(fileManager.BaseDir, artifact.RelativePath);
    }

    public async Task DeleteArtifactAsync(int artifactId, CancellationToken ct = default)
    {
        var artifact = await artifactOrm.GetByKey(artifactId)
                       ?? throw new InvalidOperationException($"Artifact {artifactId} not found");
        var generator = await generatorOrm.GetByKey(artifact.GeneratorId);
        if (generator is { AllowDeletion: false })
            throw new InvalidOperationException("Generator does not allow deleting artifacts.");

        // For PerArtifact mode, the resource owns this single file: delete the resource (with files).
        // For PerRun mode, the resource is the whole run folder; we just delete this one file.
        if (artifact.ResourceId.HasValue)
        {
            var siblingCount = (await artifactOrm.GetAll(a => a.ResourceId == artifact.ResourceId.Value)).Count;
            if (siblingCount <= 1)
            {
                await resourceService.DeleteByKeys([artifact.ResourceId.Value], deleteFiles: true);
            }
            else
            {
                var absPath = Path.Combine(fileManager.BaseDir, artifact.RelativePath);
                if (File.Exists(absPath))
                {
                    try { File.Delete(absPath); }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to delete AIGC artifact file {Path}", absPath); }
                }
            }
        }

        await artifactOrm.RemoveByKey(artifactId);
    }
}
