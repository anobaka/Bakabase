using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Downloader;

/// <summary>
/// Application orchestration for persisted producer results. A result and its chosen run are
/// committed together before scheduling, so a crash can delay delivery but cannot duplicate it.
/// Source downloaders and the shared transfer module know nothing about this workflow policy.
/// </summary>
public sealed class DownloadResultWorkflowService(BakabaseDbContext db,
    IWorkflowDefinitionService definitions, IWorkflowValidationService validation,
    BTaskManager tasks, WorkflowRunner<BakabaseDbContext> runner, IWorkflowRunResumer resumer,
    ITorrentDownloader torrents, IPlaceholderResourceService placeholders, AppService app,
    FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int> galleryCache,
    ILogger<DownloadResultWorkflowService> logger) : IAcquisitionContentsObserver
{
    // Serializes dispatch/retry inside one host; database transactions additionally protect restart boundaries.
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);
    private DbSet<DownloadResultDbModel> Results => db.Set<DownloadResultDbModel>();
    private DbSet<DownloadResultProcessingDbModel> Processing => db.Set<DownloadResultProcessingDbModel>();
    private DbSet<DownloadResultOwnerDbModel> Owners => db.Set<DownloadResultOwnerDbModel>();
    private DbSet<WorkflowRunDbModel> Runs => db.Set<WorkflowRunDbModel>();

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Set<WorkflowDefinitionDbModel>().AnyAsync(d =>
                d.TriggerKind == DownloadResultWorkflow.Trigger && d.Name == DownloadResultWorkflow.BuiltinName, ct)) return;
        var definition = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = DownloadResultWorkflow.BuiltinName,
            Description = "Download the actual contents of each saved torrent. Add resource preparation, placement and association nodes to also import them.",
            DescriptionKey = "workflow.recipe.downloadTorrentContents.description",
            TriggerKind = DownloadResultWorkflow.Trigger,
            TriggerFilterJson = "{\"kinds\":[1]}",
            Enabled = true,
            Activities = [new WorkflowActivityInputModel {Kind = DownloadResultWorkflow.FetchTorrent,
                ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail}]
        }, ct);
        await db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == definition.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true), ct);
    }

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        await DispatchGate.WaitAsync(ct);
        try
        {
            var retryBefore = DateTime.UtcNow.AddMinutes(-1);
            var candidates = await Results.AsNoTracking().Where(r => r.WorkflowDefinitionId != null &&
                !Owners.Any(o => o.DownloadTaskId == r.DownloadTaskId) &&
                !Processing.Any(p => p.DownloadResultId == r.Id &&
                    (p.WorkflowRunId != null || p.FilterDidNotMatch || p.LastAttemptAt > retryBefore)))
                .OrderBy(r => r.Id).Take(100).Select(r => r.Id).ToListAsync(ct);
            foreach (var id in candidates)
            {
                ct.ThrowIfCancellationRequested();
                await DispatchOneAsync(id, ct);
            }
            // Includes a run committed just before an enqueue failure or process shutdown.
            var pending = await Runs.AsNoTracking().Where(r => r.Status == WorkflowRunStatus.Pending &&
                Processing.Any(p => p.WorkflowRunId == r.Id)).ToListAsync(ct);
            foreach (var run in pending) await EnqueueAsync(run);
        }
        finally { DispatchGate.Release(); }
    }

    private async Task DispatchOneAsync(int id, CancellationToken ct)
    {
        var result = await Results.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        var state = await Processing.FindAsync([id], ct);
        if (state?.WorkflowRunId != null || state?.FilterDidNotMatch == true) return;
        if (state?.LastAttemptAt > DateTime.UtcNow.AddMinutes(-1)) return;
        state ??= new DownloadResultProcessingDbModel {DownloadResultId = id};
        if (db.Entry(state).State == EntityState.Detached) Processing.Add(state);
        state.LastAttemptAt = DateTime.UtcNow;
        try
        {
            var def = result.WorkflowDefinitionId is { } defId ? await definitions.GetAsync(defId) : null;
            if (def == null || !def.Enabled || def.TriggerKind != DownloadResultWorkflow.Trigger)
                throw new InvalidOperationException("The configured result workflow is missing, disabled, or uses another trigger.");
            var payload = new DownloadResultReadyPayload(result.Id, result.Kind, result.Name);
            if (!new DownloadResultReadyTrigger().Matches(payload, def.TriggerFilterJson))
            {
                state.FilterDidNotMatch = true;
                state.DispatchError = null;
                await db.SaveChangesAsync(ct);
                return;
            }
            var check = await validation.ValidateAsync(def, true, payload, ct);
            if (!check.IsValid) throw new WorkflowValidationException(check);
            var payloadJson = JsonSerializer.Serialize(payload, WorkflowJson.Options);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Ownership is permanent. Even a cancelled acquisition must never fall into independent fan-out.
            if (await Owners.AnyAsync(o => o.DownloadTaskId == result.DownloadTaskId, ct)) return;
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = def.Id, Status = WorkflowRunStatus.Pending, StartedAt = DateTime.Now,
                PayloadJson = payloadJson, PayloadSummary = result.Name
            };
            Runs.Add(run);
            await db.SaveChangesAsync(ct);
            state.WorkflowRunId = run.Id;
            state.DispatchError = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // A bad workflow remains visible on its result and does not block the next work.
            db.ChangeTracker.Clear();
            var persisted = await Processing.FindAsync([id], ct);
            if (persisted?.WorkflowRunId != null) return;
            persisted ??= new DownloadResultProcessingDbModel {DownloadResultId = id};
            if (db.Entry(persisted).State == EntityState.Detached) Processing.Add(persisted);
            persisted.LastAttemptAt = DateTime.UtcNow;
            persisted.DispatchError = ex.Message;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Could not dispatch download result {ResultId}", id);
        }
    }

    private Task EnqueueAsync(WorkflowRunDbModel run) => tasks.Enqueue(BTaskBuilder.Create($"workflow.run.{run.Id}")
        .Named($"Workflow #{run.WorkflowDefinitionId} run #{run.Id}")
        .IgnoreIfExists().ConflictsWith($"workflow.definition.{run.WorkflowDefinitionId}")
        .Run(args => runner.ExecuteAsync(run.Id, args)));

    public async Task RetryAsync(int id, CancellationToken ct = default)
    {
        await DispatchGate.WaitAsync(ct);
        try
        {
            var result = await Results.SingleOrDefaultAsync(r => r.Id == id, ct)
                ?? throw new InvalidOperationException("The download result no longer exists.");
            if (await Owners.AnyAsync(o => o.DownloadTaskId == result.DownloadTaskId, ct))
                throw new InvalidOperationException("Retry the original acquisition task to continue this result.");
            var state = await Processing.FindAsync([id], ct);
            if (state?.WorkflowRunId is { } runId)
            {
                await resumer.RequeueAsync(runId, ct);
                return;
            }
            if (state != null)
            {
                state.LastAttemptAt = null;
                state.FilterDidNotMatch = false;
                state.DispatchError = null;
                await db.SaveChangesAsync(ct);
            }
            if (result.WorkflowDefinitionId == null)
                throw new InvalidOperationException("This task was configured to save results only.");
            await DispatchOneAsync(id, ct);
            var run = await Processing.Where(p => p.DownloadResultId == id && p.WorkflowRunId != null)
                .Join(Runs, p => p.WorkflowRunId, r => r.Id, (p, r) => r).FirstOrDefaultAsync(ct);
            if (run != null) await EnqueueAsync(run);
        }
        finally { DispatchGate.Release(); }
    }

    private async Task<(DownloadResultDbModel Result, DownloadResultProcessingDbModel State)> GetForRunAsync(
        int id, int runId, CancellationToken ct)
    {
        var result = await Results.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException("The download result no longer exists.");
        var state = await Processing.SingleOrDefaultAsync(p => p.DownloadResultId == id, ct);
        if (state?.WorkflowRunId != runId || await Owners.AnyAsync(o => o.DownloadTaskId == result.DownloadTaskId, ct))
            throw new InvalidOperationException("Start or retry this workflow through its download result; it belongs to another run.");
        return (result, state);
    }

    public async Task DownloadContentsAsync(int id, int runId, int timeoutMinutes,
        Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (timeoutMinutes is < 1 or > 43200) throw new ArgumentOutOfRangeException(nameof(timeoutMinutes));
        var (result, state) = await GetForRunAsync(id, runId, ct);
        // Piece integrity on transfer retries belongs to the shared BitTorrent service. A fully
        // completed result can be reused only while its recorded files still exist.
        if (state.ContentsReadyAt != null && Directory.Exists(state.ContentsDirectory) &&
            ReadFiles(state.ContentsFilesJson) is {Count: > 0} oldFiles && oldFiles.All(File.Exists)) return;
        string directory;
        IReadOnlyList<string> files;
        if (result.Kind == DownloadResultKind.TorrentMetadata)
        {
            await using var stream = File.OpenRead(result.Path);
            var metadata = await TorrentMetadata.ReadBoundedAsync(stream, ct);
            TorrentMetadata.Validate(metadata);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(metadata)), result.Fingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new IOException("The saved torrent metadata changed; acquire its result again.");
            var name = string.Concat(result.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c)).Trim();
            if (name.Length > 100) name = name[..100];
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            var workDirectory = Path.Combine(result.DownloadDirectory, $"{name} [{id}]");
            var downloaded = await torrents.DownloadTorrentAsync(metadata, workDirectory,
                TimeSpan.FromMinutes(timeoutMinutes), progress, ct);
            directory = downloaded.Directory;
            files = downloaded.Files;
        }
        else if (result.Kind == DownloadResultKind.LocalFiles)
        {
            directory = result.Path;
            files = ReadFiles(result.FilesJson);
        }
        else throw new InvalidOperationException("This download result has an unsupported type.");
        if (!Directory.Exists(directory) || files.Count == 0 || files.Any(f => !File.Exists(f)))
            throw new IOException("The result does not contain completed local files.");
        state.ContentsDirectory = directory;
        state.ContentsFilesJson = JsonSerializer.Serialize(files, WorkflowJson.Options);
        state.ContentsReadyAt = DateTime.UtcNow;
        await UpdateGalleryAsync(result, directory, null, ct);
        await db.SaveChangesAsync(ct);
        galleryCache.ClearCache();
    }

    public async Task<AcquisitionWorkItem> PrepareResourceAsync(int id, int runId, string name, CancellationToken ct)
    {
        var (result, state) = await GetForRunAsync(id, runId, ct);
        if (state.ContentsReadyAt == null && result.Kind == DownloadResultKind.LocalFiles)
        {
            await DownloadContentsAsync(id, runId, 240, null, ct);
        }
        if (state.ContentsReadyAt == null || !Directory.Exists(state.ContentsDirectory))
            throw new InvalidOperationException("Download the torrent contents before preparing this resource for import.");
        var files = ReadFiles(state.ContentsFilesJson);
        if (files.Count == 0 || files.Any(f => !File.Exists(f))) throw new IOException("Downloaded files are missing.");
        if (state.ResourceId == null)
        {
            var resource = result.ThirdPartyId == ThirdPartyId.ExHentai
                ? await placeholders.CreateOrMatchByExternalIdentity(ResourceSource.ExHentai, result.SourceKey,
                    new KnownItemDetail(result.Name), ct)
                : await placeholders.CreateByTitle(result.Name, ct);
            state.ResourceId = resource.ResourceId;
            await db.SaveChangesAsync(ct);
        }
        var working = Path.Combine(app.AppDataDirectory, "download-result-workflows", id.ToString());
        Directory.CreateDirectory(working);
        var contentDirectory = state.ContentsDirectory;
        if (result.Kind == DownloadResultKind.LocalFiles)
        {
            // An older source task may share its directory with other works. Placement owns only
            // the explicit result files, never everything that happens to be beside them.
            contentDirectory = Path.Combine(working, "content");
            Directory.CreateDirectory(contentDirectory);
            var copied = new List<string>();
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(state.ContentsDirectory!, file);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                    throw new IOException("A result file is outside its content directory.");
                var target = Path.Combine(contentDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var input = File.OpenRead(file))
                await using (var output = File.Create(target + ".copying"))
                    await input.CopyToAsync(output, ct);
                File.Move(target + ".copying", target, true);
                copied.Add(target);
            }
            files = copied;
        }
        return new AcquisitionWorkItem
        {
            ResourceId = state.ResourceId.Value, LeadKind = AcquisitionLeadKind.Manual, LeadValue = "",
            Title = result.Name, WorkingName = string.IsNullOrWhiteSpace(name) ? result.Name : name,
            WorkingDirectory = working, ExtractedDirectory = contentDirectory,
            Files = files, PreserveDirectoryStructure = true,
            Variables = new Dictionary<string, string> {{"downloadResultId", id.ToString()}}
        };
    }

    public Task OnContentsReadyAsync(AcquisitionStepContext context, AcquisitionWorkItem item,
        string directory, IReadOnlyList<string> files, CancellationToken ct)
    {
        if (!item.Variables.TryGetValue("downloadResultId", out var resultIdText) ||
            !int.TryParse(resultIdText, out var resultId)) return Task.CompletedTask;
        if (context.WorkflowRunId is not > 0)
            throw new InvalidOperationException("A download result location requires its workflow run.");
        return RecordContentsAsync(resultId, context.WorkflowRunId.Value, item.ResourceId, directory, files, ct);
    }

    /// <summary>Tracks actual files and subsequent moves independently of resource materialization.</summary>
    public async Task RecordContentsAsync(int id, int runId, int resourceId, string directory,
        IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var result = await Results.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException("The download result no longer exists.");
        var state = await Processing.SingleOrDefaultAsync(p => p.DownloadResultId == id, ct);
        var owner = await Owners.AsNoTracking().SingleOrDefaultAsync(o => o.DownloadTaskId == result.DownloadTaskId, ct);
        if (owner != null)
        {
            if (owner.WorkflowRunId != runId || owner.ResourceId != resourceId)
                throw new InvalidOperationException("The downloaded content belongs to a different acquisition run or resource.");
        }
        else if (state?.WorkflowRunId != runId || state.ResourceId != resourceId)
        {
            throw new InvalidOperationException("The downloaded content belongs to a different result workflow or resource.");
        }
        var root = Path.GetFullPath(directory);
        var actualFiles = files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        if (!Directory.Exists(root) || actualFiles.Length == 0)
            throw new IOException("The result does not contain completed local files.");
        foreach (var file in actualFiles)
        {
            var relative = Path.GetRelativePath(root, file);
            if (!File.Exists(file) || Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("A result file is missing or outside its content directory.");
        }
        state ??= new DownloadResultProcessingDbModel {DownloadResultId = id};
        if (db.Entry(state).State == EntityState.Detached) Processing.Add(state);
        state.ContentsDirectory = root;
        state.ContentsFilesJson = JsonSerializer.Serialize(actualFiles, WorkflowJson.Options);
        state.ContentsReadyAt ??= DateTime.UtcNow;
        // Resource association still belongs to MaterializeStep. The gallery may already open
        // its actual files while the workflow waits for a placement decision or an import retry.
        await UpdateGalleryAsync(result, root, null, ct);
        await db.SaveChangesAsync(ct);
        galleryCache.ClearCache();
    }

    public async Task RecordMaterializedAsync(int id, int runId, int resourceId, string directory,
        IReadOnlyList<string> files, CancellationToken ct)
    {
        await RecordContentsAsync(id, runId, resourceId, directory, files, ct);
        var result = await Results.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        var state = await Processing.SingleAsync(p => p.DownloadResultId == id, ct);
        state.ResourceId = resourceId;
        await UpdateGalleryAsync(result, directory, resourceId, ct);
        await db.SaveChangesAsync(ct);
        galleryCache.ClearCache();
    }

    private async Task UpdateGalleryAsync(DownloadResultDbModel result, string directory, int? resourceId, CancellationToken ct)
    {
        if (result.ThirdPartyId != ThirdPartyId.ExHentai) return;
        var parts = result.SourceKey.Split('/');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId)) return;
        var gallery = await db.Set<ExHentaiGalleryDbModel>().SingleOrDefaultAsync(g =>
            g.GalleryId == galleryId && g.GalleryToken == parts[1], ct);
        if (gallery == null) return;
        gallery.LocalPath = directory;
        gallery.IsDownloaded = true;
        gallery.ResourceId = resourceId ?? gallery.ResourceId;
        gallery.UpdatedAt = DateTime.Now;
    }

    private static IReadOnlyList<string> ReadFiles(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, WorkflowJson.Options) ?? [];
}
