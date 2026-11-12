using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Steps;

public partial class UnpackStep
{
    // The new layout operates on an isolated result. Keep the old in-place path for already saved nodes.
    private static async Task<AcquisitionStepOutcome> UnpackInWorkspaceAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        Config config, AcquisitionOptions options, IArchiveExtractionService archives, string? password, CancellationToken ct)
    {
        var root = item.ExtractedDirectory ?? ctx.WorkingDirectory;
        AcquisitionStoragePaths.EnsureSourceAllowed(ctx, item, root);
        if (!Directory.Exists(root)) return new AcquisitionStepOutcome.Skip("There is nothing here to unpack.", item);
        var state = await Workspace(ctx, item, root, ctx.ServiceProvider.GetService<FileProcessingWorkspaceResolver>(), ct);
        var recursiveRoot = Path.Combine(state, "unpack-layout");
        var files = item.Files;
        if (files.Count == 0)
        {
            // A replay after explicit source recycling can still resolve the same completed checkpoint.
            var checkpoint = FileProcessingFiles.Within(Path.Combine(recursiveRoot, "recursive-state.json"), state);
            if (File.Exists(checkpoint))
            {
                await using var stream = File.OpenRead(checkpoint);
                var saved = await JsonSerializer.DeserializeAsync<RecursiveArchiveExecutor.State>(stream, Json, ct)
                    ?? throw new InvalidDataException("The unpack checkpoint is unreadable.");
                files = saved.Inputs.Select(f => FileProcessingFiles.Within(Path.Combine(root, f.Path), root)).ToArray();
            }
            else files = FileProcessingFiles.Enumerate(root, excludeManagedWorkspaces: true);
        }
        if (files.Count == 0) return new AcquisitionStepOutcome.Skip("There is nothing here to unpack.", item);
        foreach (var file in files) AcquisitionStoragePaths.EnsureSourceAllowed(ctx, item, file);
        item = item with {ProcessingStateDirectory = state, Files = files};
        var executor = new RecursiveArchiveExecutor(new PasswordCandidates(archives, ctx, options, config, item),
            writeGuard: ctx.ServiceProvider.GetService<IStorageWriteGuard>());
        var answer = password != null && item.Variables.TryGetValue("unpackPendingArchiveId", out var archiveId)
            ? new RecursiveArchivePasswordAnswer(archiveId, password) : null;
        var result = await executor.ExecuteAsync(root, files, recursiveRoot, new() {MaxDepth = Math.Max(1, config.MaxDepth)},
            answer: answer, ct: ct, extractionLayout: config.ExtractionLayout);
        if (!result.Completed)
        {
            item = item with {Variables = With(item.Variables, "unpackPendingArchiveId", result.ArchiveId)};
            if (result.WaitReason == "PasswordUnknown")
                return new AcquisitionStepOutcome.Suspend(AcquisitionWaitReason.PasswordUnknown, JsonSerializer.Serialize(
                    new Prompt(result.Message ?? "Archive", result.TriedPasswords ?? []), Json), item);
            return new AcquisitionStepOutcome.Suspend(AcquisitionWaitReason.ExtractionPlanUnknown, JsonSerializer.Serialize(
                new PlanPrompt("ExtractionPlanUnknown", result.Message ?? "Review the extraction plan.", null, "{\"requirement\":\"unknown\",\"steps\":[]}"), Json), item);
        }
        if (result.Lineage is not {Count: > 0}) return new AcquisitionStepOutcome.Skip("Nothing here is an archive.", item);
        if (config.DeleteArchive && result.SourceArchiveFiles is {Count: > 0})
            await RecycleReplacedArchivesAsync(ctx, item, root, result, ct);
        return new AcquisitionStepOutcome.Continue(item with {ExtractedDirectory = result.OutputDirectory,
            Files = result.Files, PreserveDirectoryStructure = true});
    }

    private static async Task RecycleReplacedArchivesAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        string root, RecursiveArchiveResult result, CancellationToken ct)
    {
        if (result.FileLineage is not {InputManifest: { } inputs, OutputManifest: { } outputs} lineage ||
            result.OutputDirectory == null || !(await ArtifactManifestFiles.ValidateAsync(result.OutputDirectory, outputs, ct)).Available)
            return;
        var candidates = result.SourceArchiveFiles!.Where(File.Exists).ToArray();
        var store = ctx.ServiceProvider.GetService<IArtifactOperationStore>();
        if (store == null || candidates.Length == 0) return;
        await using var fence = await store.AcquireMaintenanceAsync(ct);
        if (await store.HasRegisteredFilesAsync(candidates, ct)) return;
        var measured = await ArtifactManifestFiles.CaptureAsync(root, candidates, ct);
        var recorded = inputs.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        var replaced = lineage.Entries.Where(e => e.Replaced && e.OutputRelativePaths.Count > 0)
            .Select(e => e.InputRelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in measured.Entries)
        {
            if (!replaced.Contains(entry.RelativePath) || !recorded.TryGetValue(entry.RelativePath, out var expected) ||
                !FileDispositionProofs.SameEntry(entry, expected)) continue;
            var file = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
            AcquisitionStoragePaths.EnsureSourceAllowed(ctx, item, file);
            FileUtils.Delete(file, true, true);
        }
    }

    private sealed class PasswordCandidates(IArchiveExtractionService archives, AcquisitionStepContext ctx,
        AcquisitionOptions options, Config config, AcquisitionWorkItem item) : IArchiveExtractionService
    {
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            var known = await BuildCandidatesAsync(ctx, options, config, item, entryFile, null);
            return await archives.ProbePasswordAsync(entryFile, candidates.Concat(known).Distinct().ToArray(), output, ct);
        }
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            archives.ExtractAsync(request, progress, ct);
    }
}
