using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    public async Task<AcquisitionStageDirectorySnapshot> GetStageDirectoriesAsync(int id, CancellationToken ct)
    {
        var row = await StageDirectoryTaskAsync(id, ct);
        var plan = Plan(row); var mapping = await CurrentStageDirectoriesAsync(row, plan, ct);
        var issue = await StageDirectoryActivityIssueAsync(row, ct);
        return new(id, row.PlanRevision, plan.Parts.Select(p =>
        {
            var part = mapping.Parts.GetValueOrDefault(p.Id);
            return new AcquisitionStageDirectoryPartSnapshot(p.Id, p.Title, part?.Download?.Directory,
                part?.Processing?.Directory, part?.Delivery?.Directory);
        }).ToArray(), issue == null, issue == null ? [] : [issue], State(row).DirectoryChange?.Input);
    }

    public async Task<AcquisitionStageDirectoryChangePreview> PreviewStageDirectoriesAsync(int id,
        AcquisitionStageDirectoryChangeInput input, Func<string, Task>? guard, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id);
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        try
        {
            var row = await StageDirectoryTaskAsync(id, ct); StageDirectoryRevision(row, input.ExpectedRevision);
            await RequireStageDirectoryInactiveAsync(row, ct);
            var pending = State(row).DirectoryChange;
            var journal = pending ?? await PrepareStageDirectoryChangeAsync(row, input, guard, ct);
            if (pending != null && !SameStageDirectoryRequest(pending.Input, input))
                throw new InvalidOperationException("STAGE_DIRECTORY_CHANGE_PENDING");
            return new(id, row.PlanRevision, StageDirectoryItems(journal), true);
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionStageDirectoryChangeResult> ChangeStageDirectoriesAsync(int id,
        AcquisitionStageDirectoryChangeInput input, Func<string, Task>? guard, CancellationToken ct,
        IFileArtifactMaterializer? materializer = null)
    {
        var gate = AcquisitionTaskGate.For(id);
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("STAGE_DIRECTORY_TASK_ACTIVE");
        try
        {
            var row = await StageDirectoryTaskAsync(id, ct); StageDirectoryRevision(row, input.ExpectedRevision);
            await RequireStageDirectoryInactiveAsync(row, ct);
            if (artifacts == null) throw new InvalidOperationException("STAGE_DIRECTORY_UNAVAILABLE");
            await using var fence = await artifacts.AcquireMaintenanceAsync(ct);
            var journal = State(row).DirectoryChange;
            if (journal != null && !SameStageDirectoryRequest(journal.Input, input))
                throw new InvalidOperationException("STAGE_DIRECTORY_CHANGE_PENDING");
            journal ??= await PrepareStageDirectoryChangeAsync(row, input, guard, ct);
            if (journal.Moves.Count == 0) return new(id, row.PlanRevision, journal.Directories, []);
            async Task Persist()
            {
                row.BuilderStateJson = WithStageDirectoryJournal(row.BuilderStateJson, journal);
                row.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            await Persist();
            var owners = await StageDirectoryOwnersAsync(row, ct);
            for (var index = 0; index < journal.Moves.Count; index++)
            {
                var move = journal.Moves[index]; if (move.Completed) continue;
                async Task Guard()
                {
                    await RequireStageDirectoryInactiveAsync(row, ct);
                    await StageDirectoryReferencesAsync(row, owners, move.PartId, move.Directory, ct);
                    if (guard != null) await guard(move.Directory);
                    if (move.MoveFiles && move.SourceDirectory != null)
                    {
                        storage.EnsureTreeMutationAllowed(move.SourceDirectory);
                        await StageDirectoryReferencesAsync(row, owners, move.PartId, move.SourceDirectory, ct);
                        if (guard != null) await guard(move.SourceDirectory);
                    }
                }
                await Guard(); ValidateStageDirectoryPath(move.Directory);
                Directory.CreateDirectory(move.Directory);
                await AcquisitionStageDirectoryFileMove.ExecuteAsync(move, journal.Id,
                    materializer ?? new FileArtifactMaterializer(), Persist, Guard, ct);
                if (move.MoveFiles && move.SourceDirectory != null && Directory.Exists(move.SourceDirectory))
                {
                    var remaining = AcquisitionStageDirectoryFileMove.Inspect(move.SourceDirectory,
                        await StageDirectoryExclusionsAsync(move.SourceDirectory, ct), ct);
                    if (remaining.Files.Count != 0) throw new IOException("STAGE_DIRECTORY_MOVE_CHANGED");
                }
                journal.Moves[index] = move with {Completed = true}; await Persist();
            }
            // Commit one explicit current-location revision. Past artifacts, worksets and
            // attempts retain their original paths; no read-time relocation is installed.
            await RequireStageDirectoryInactiveAsync(row, ct);
            foreach (var move in journal.Moves)
                await StageDirectoryReferencesAsync(row, owners, move.PartId, move.Directory, ct);
            var history = ReadHistory(row);
            if (history.Count >= 100) throw new InvalidOperationException("STAGE_DIRECTORY_REVISION_LIMIT");
            if (history.All(h => h.Revision != row.PlanRevision))
                history.Add(new(row.PlanRevision, Plan(row), Sources(row), DateTime.UtcNow, "directories.previous", row.WorkflowRunId));
            var plan = Plan(row) with {StageDirectories = journal.Directories};
            row.PlanRevision++; row.PlanJson = Serialize(plan);
            history.Add(new(row.PlanRevision, plan, Sources(row), DateTime.UtcNow, "directories.changed", row.WorkflowRunId));
            row.PlanHistoryJson = Serialize(history);
            row.BuilderStateJson = WithStageDirectoryJournal(row.BuilderStateJson, null); row.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync(CancellationToken.None);
            return new(id, row.PlanRevision, journal.Directories, StageDirectoryItems(journal));
        }
        finally {gate.Release();}
    }

    private async Task<AcquisitionStageDirectoryChangeJournal> PrepareStageDirectoryChangeAsync(
        AcquisitionTaskDbModel row, AcquisitionStageDirectoryChangeInput input, Func<string, Task>? guard, CancellationToken ct)
    {
        var plan = Plan(row); var prior = await CurrentStageDirectoriesAsync(row, plan, ct);
        if (input.Parts == null || input.Parts.Count == 0 || input.Parts.Count > plan.Parts.Count ||
            input.Parts.Select(p => p.PartId).Distinct(StringComparer.Ordinal).Count() != input.Parts.Count ||
            input.Parts.Any(p => !prior.Parts.ContainsKey(p.PartId))) throw new ArgumentException("STAGE_DIRECTORY_SELECTION_INVALID");
        var history = ReadHistory(row);
        if (history.Count + (history.Any(h => h.Revision == row.PlanRevision) ? 1 : 2) > 100)
            throw new InvalidOperationException("STAGE_DIRECTORY_REVISION_LIMIT");
        var parts = prior.Parts.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var moves = new List<AcquisitionStageDirectoryMove>(); var owners = await StageDirectoryOwnersAsync(row, ct);
        foreach (var part in input.Parts)
        foreach (var (stage, choice) in new[] {("download", part.Download), ("processing", part.Processing), ("delivery", part.Delivery)})
        {
            if (choice == null) continue;
            ValidateStageDirectoryPath(choice.Directory);
            var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(choice.Directory));
            var current = StageDirectoryEntry(parts[part.PartId], stage);
            var unchanged = current != null && AcquisitionStageDirectoryFileMove.Same(current.Directory, destination);
            if (unchanged && current!.BindingId != null) continue;
            if (!unchanged && stage == "download") await RequireDownloadDirectoryMovableAsync(row, owners, part.PartId, ct);
            if (guard != null) await guard(destination);
            await StageDirectoryReferencesAsync(row, owners, part.PartId, destination, ct);
            List<AcquisitionStageDirectoryMoveFile> files = []; IReadOnlyList<string> empty = [];
            if (!unchanged && choice.MoveFiles && current != null && Directory.Exists(current.Directory))
            {
                storage.EnsureTreeMutationAllowed(current.Directory); ArtifactManifestFiles.RejectLinks(current.Directory);
                if (ArtifactCleanupPathReferences.StrictlyUnder(destination, current.Directory))
                    throw new ArgumentException("STAGE_DIRECTORY_OVERLAP");
                if (guard != null) await guard(current.Directory);
                await StageDirectoryReferencesAsync(row, owners, part.PartId, current.Directory, ct);
                var excluded = await StageDirectoryExclusionsAsync(current.Directory, ct);
                (files, empty) = AcquisitionStageDirectoryFileMove.Inspect(current.Directory, excluded, ct);
            }
            else if (!unchanged && choice.MoveFiles && current != null && current.Evidence is not ("taskAllocation" or "taskLayout" or "plannedDelivery"))
                throw new IOException("STAGE_DIRECTORY_SOURCE_UNAVAILABLE");
            var move = new AcquisitionStageDirectoryMove(part.PartId, stage, current?.Directory, destination,
                choice.MoveFiles && !unchanged && current != null && Directory.Exists(current.Directory), files, empty);
            AcquisitionStageDirectoryFileMove.CheckTargets(move);
            moves.Add(move);
            // Choosing a directory makes it authoritative, not exclusively owned. Existing
            // unrelated files never acquire cleanup authorization from this setting.
            var entry = unchanged && current!.Ownership == "exclusive"
                ? current with {BindingId = Guid.NewGuid().ToString("N")}
                : new AcquisitionStageDirectory(destination, "borrowed", "userDirectoryRevision")
                    {BindingId = Guid.NewGuid().ToString("N")};
            var saved = plan.StageDirectories?.Parts.GetValueOrDefault(part.PartId);
            var allocated = saved == null ? null : StageDirectoryEntry(saved, stage);
            if (allocated is {Ownership: "exclusive", Allocation: {} scope} && workspaces != null &&
                scope.OwnerKind == "acquisition" && scope.OwnerId == row.Id.ToString(CultureInfo.InvariantCulture) &&
                scope.Role == stage + "-" + part.PartId && AcquisitionStageDirectoryFileMove.Same(scope.Directory, destination) &&
                AcquisitionStageDirectoryFileMove.Same(allocated.Directory, destination) &&
                await workspaces.HasScopedDirectoryAsync(scope, ct))
                entry = allocated with {BindingId = entry.BindingId};
            parts[part.PartId] = stage switch {"download" => parts[part.PartId] with {Download = entry},
                "processing" => parts[part.PartId] with {Processing = entry}, _ => parts[part.PartId] with {Delivery = entry}};
        }
        var selected = parts.Values.SelectMany(p => new[] {p.Download, p.Processing, p.Delivery})
            .Where(p => p != null).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p!.Directory))).ToArray();
        for (var i = 0; i < selected.Length; i++) for (var j = i + 1; j < selected.Length; j++)
            if (StageDirectoryOverlap(selected[i], selected[j])) throw new ArgumentException("STAGE_DIRECTORY_OVERLAP");
        return new(Guid.NewGuid().ToString("N"), row.PlanRevision, input,
            prior with {Parts = parts}, moves, DateTime.UtcNow);
    }

    private async Task<AcquisitionTaskDbModel> StageDirectoryTaskAsync(int id, CancellationToken ct)
    {
        var row = await Tasks.SingleOrDefaultAsync(t => t.Id == id && t.BuilderId != null && t.DeletedAt == null, ct)
            ?? throw new InvalidOperationException("STAGE_DIRECTORY_TASK_UNAVAILABLE");
        await db.Entry(row).ReloadAsync(ct);
        if (row.PlanJson == null || row.DeletedAt != null) throw new InvalidOperationException("STAGE_DIRECTORY_TASK_UNAVAILABLE");
        return row;
    }
    private async Task<AcquisitionStageDirectories> CurrentStageDirectoriesAsync(AcquisitionTaskDbModel row, AcquisitionPlan plan, CancellationToken ct)
    {
        var run = row.WorkflowRunId is {} runId ? await db.WorkflowRuns.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        var mapping = AcquisitionStageDirectoryResolver.FromRecorded(row.Id, plan,
            AcquisitionEffectiveSourceProjection.Read(run?.CurrentItemJson));
        return await ProjectInitialDownloadDirectoryAsync(row, plan, mapping, run, ct);
    }
    private static AcquisitionStageDirectory? StageDirectoryEntry(AcquisitionPartDirectories part, string stage) =>
        stage switch {"download" => part.Download, "processing" => part.Processing, _ => part.Delivery};
    private static bool StageDirectoryOverlap(string a, string b) => AcquisitionStageDirectoryFileMove.Same(a, b) ||
        ArtifactCleanupPathReferences.StrictlyUnder(a, b) || ArtifactCleanupPathReferences.StrictlyUnder(b, a);
    private static void StageDirectoryRevision(AcquisitionTaskDbModel row, int revision)
    {if (row.PlanRevision != revision) throw new InvalidOperationException("STAGE_DIRECTORY_REVISION_CHANGED");}
    private static bool SameStageDirectoryRequest(AcquisitionStageDirectoryChangeInput a, AcquisitionStageDirectoryChangeInput b) =>
        Serialize(a) == Serialize(b);
    private static string WithStageDirectoryJournal(string? state, AcquisitionStageDirectoryChangeJournal? journal)
    {
        var document = JsonNode.Parse(state ?? "{}") as JsonObject ?? throw new InvalidDataException("STAGE_DIRECTORY_UNAVAILABLE");
        if (journal == null) document.Remove("directoryChange");
        else document["directoryChange"] = JsonSerializer.SerializeToNode(journal, Json);
        return document.ToJsonString(Json);
    }
    private static AcquisitionStageDirectoryChangeItem[] StageDirectoryItems(AcquisitionStageDirectoryChangeJournal journal) =>
        journal.Moves.Select(m => new AcquisitionStageDirectoryChangeItem(m.PartId, m.Stage, m.SourceDirectory,
            m.Directory, m.MoveFiles, true, m.Files.Count, m.Files.Sum(f => f.Length), true)).ToArray();
    private void ValidateStageDirectoryPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || directory.Any(char.IsControl))
            throw new ArgumentException("STAGE_DIRECTORY_PATH_INVALID");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (AcquisitionStageDirectoryFileMove.Same(full, Path.GetPathRoot(full)!)) throw new ArgumentException("STAGE_DIRECTORY_PATH_INVALID");
        storage.EnsurePathAllowed(full); storage.EnsureTreeMutationAllowed(full); ArtifactManifestFiles.RejectLinks(full);
        if (File.Exists(full)) throw new IOException("STAGE_DIRECTORY_TARGET_CONFLICT");
        if (!Directory.Exists(full) && !Directory.Exists(Path.GetDirectoryName(full)))
            throw new IOException("STAGE_DIRECTORY_TARGET_UNAVAILABLE");
    }
    private async Task<HashSet<string>> StageDirectoryExclusionsAsync(string root, CancellationToken ct)
    {
        var excluded = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        if (workspaces != null)
            foreach (var proof in await workspaces.OwnedDirectoriesUnderAsync(root, ct))
                if (await workspaces.MatchesOwnedDirectoryAsync(proof, ct)) excluded.Add(proof.Directory);
                else throw new IOException("STAGE_DIRECTORY_SOURCE_UNAVAILABLE");
        return excluded;
    }
}
