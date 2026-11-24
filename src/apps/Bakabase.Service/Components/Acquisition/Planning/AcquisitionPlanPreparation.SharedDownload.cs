using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private async Task<AcquisitionSharedDownloadPreview?> ReviewSharedDownloadAsync(AcquisitionTaskDbModel row,
        AcquisitionPlan? plan, AcquisitionPreparationPreview preparation, CancellationToken ct, Func<string, Task>? guardDirectory)
    {
        if (!preparation.CanConfirm || preparation.AllowUnavailableSourceIds.Count != 0 ||
            plan == null || plan.Parts.Count < 2 || plan.PublishRetainedFiles) return null;

        var state = State(row);
        if (state.DirectoryChange != null || state.ProcessingStageInputId != null || state.ProcessingStageCommitId != null ||
            state.LocalRecoveryQueue != null || state.LocalFilesRecovery != null || state.RegisteredDownload != null ||
            state.LocalDownloadIntake != null) return null;

        var first = plan.Parts[0];
        var signature = SharedDownloadInstructions(first);
        if (plan.Parts.Any(p => p.ContinueFrom != null || p.DeliveryProjectionFingerprint != null ||
                p.SelectionMode == "automatic" && p.Sources.Count > 1 ||
                SharedDownloadInstructions(p) != signature)) return null;

        // Require each saved choice, with the same resolved account generation and method scope.
        // A provider's canonical reuse key alone can omit parameters that select remote files.
        var groups = preparation.Groups.Where(g => g.SelectedSourceIds.Count != 0).ToDictionary(g => g.Id, StringComparer.Ordinal);
        if (groups.Count != plan.Parts.Count) return null;
        var identities = new HashSet<(string Fingerprint, string? MethodScope, string? Version)>();
        foreach (var part in plan.Parts)
        {
            if (part.SelectedSourceId == null || !groups.TryGetValue(part.Id, out var group) ||
                !group.SelectedSourceIds.SequenceEqual(new[] {part.SelectedSourceId})) return null;
            var selected = group.Sources.SingleOrDefault(s => s.Candidate.Id == part.SelectedSourceId);
            if (selected?.CanSelect != true || selected.ResolvedSource is not {} resolved) return null;
            identities.Add((CloudSourceIdentity.Fingerprint(resolved), resolved.MethodScope, resolved.Version));
        }
        if (identities.Count != 1) return null;

        var directories = await CurrentStageDirectoriesAsync(row, plan, ct);
        var review = new List<AcquisitionSharedDownloadGroup>(plan.Parts.Count);
        foreach (var part in plan.Parts)
        {
            if (!directories.Parts.TryGetValue(part.Id, out var paths) || paths.Delivery is not {} delivery)
                return null;
            foreach (var stage in new[] {paths.Download, paths.Processing, paths.Delivery})
            {
                if (stage == null) continue;
                if (stage.Evidence is not ("plannedDelivery" or "taskLayout" or "taskAllocation") ||
                    stage.Ownership == "borrowed" && stage.Evidence != "plannedDelivery") return null;
                if (!await EmptySharedDownloadDirectoryAsync(stage.Directory, guardDirectory)) return null;
            }
            review.Add(new(part.Id, part.Title, delivery.Directory));
        }

        var archiveSteps = AcquisitionPlanCompiler.Processing(first).Steps
            .Where(s => s.Op == "extractArchive").ToArray();
        var canUseArchiveDirectories = archiveSteps.Length != 0 &&
            plan.DeliveryLayout?.Settings.ContentLayout?.CollapseSingleRootDirectory != true &&
            archiveSteps.All(s => s.ExtractionLayout is null or FileExtractionLayouts.ArchiveDirectory);
        var alreadyUsesArchiveDirectories = canUseArchiveDirectories && archiveSteps.All(s =>
            (s.ExtractionLayout ?? plan.ExtractionLayout) == FileExtractionLayouts.ArchiveDirectory);
        var (identity, methodScope, version) = identities.Single();
        var fingerprint = CloudOperationService.Key(Serialize(new
        {
            row.Id, row.PlanRevision, Identity = identity, MethodScope = methodScope, Version = version,
            Groups = review, CanUseArchiveDirectories = canUseArchiveDirectories,
            AlreadyUsesArchiveDirectories = alreadyUsesArchiveDirectories
        }));
        return new(review, canUseArchiveDirectories, alreadyUsesArchiveDirectories, fingerprint);
    }

    private async Task<AcquisitionPlanTaskDetail> ConfirmSharedDownloadAsync(AcquisitionTaskDbModel row,
        AcquisitionPreparationInput input, CancellationToken ct, Func<string, Task>? guardDirectory)
    {
        var choice = input.SharedDownload!;
        if (string.IsNullOrWhiteSpace(choice.KeepPartId) || choice.ExpectedFingerprint is not {Length: 64} ||
            input.Sources != null || input.Relationship != null ||
            input.SelectedSourceIds != null || input.AllowUnavailableSourceIds != null)
            throw new ArgumentException("SHARED_DOWNLOAD_SELECTION_INVALID");
        var reviewInput = new AcquisitionPreparationInput(input.ExpectedRevision, CheckAvailability: false);
        var snapshot = await ReadPreparationAsync(row, reviewInput, ct);
        var preview = await CheckPreparationAsync(row, reviewInput, snapshot, false, ct);
        var shared = await ReviewSharedDownloadAsync(row, snapshot.Plan, preview, ct, guardDirectory) ??
            throw new InvalidOperationException("SHARED_DOWNLOAD_UNAVAILABLE");
        if (choice.ExpectedFingerprint != shared.Fingerprint) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
        var keep = shared.Groups.SingleOrDefault(p => p.PartId == choice.KeepPartId);
        if (keep == null || choice.UseArchiveDirectories && !shared.CanUseArchiveDirectories)
            throw new ArgumentException("SHARED_DOWNLOAD_SELECTION_INVALID");

        var previous = snapshot.Plan!;
        var retained = previous.Parts.Single(p => p.Id == keep.PartId);
        var removed = previous.Parts.Where(p => p.Id != retained.Id).ToArray();
        var oldGroups = removed.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var oldSources = removed.SelectMany(p => p.Sources).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var state = State(row);
        row.BuilderStateJson = Serialize(state with
        {
            // Earlier plans retain the original groups and all their source/processing evidence.
            DraftSources = state.DraftSources.Where(s => !oldSources.Contains(s.Id))
                .Select(s => s.GroupId != null && oldGroups.Contains(s.GroupId) ? s with {GroupId = retained.Id} : s).ToList(),
            Issues = preview.Requirements.Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList()
        });
        var plan = previous with
        {
            Parts = [retained],
            ExtractionLayout = choice.UseArchiveDirectories ? FileExtractionLayouts.ArchiveDirectory : previous.ExtractionLayout
        };
        SavePlan(row, plan, Sources(row), reason: choice.UseArchiveDirectories
            ? "Shared download confirmed with independent archive directories" : "Shared download confirmed");
        var saved = await CurrentStageDirectoriesAsync(row, Plan(row), ct);
        if (saved.Parts.GetValueOrDefault(retained.Id)?.Delivery is not {} destination ||
            !ArtifactManifestFiles.PathComparer.Equals(destination.Directory, keep.DeliveryDirectory))
            throw new InvalidOperationException("SHARED_DOWNLOAD_UNAVAILABLE");
        await SaveDraftEditAsync(row, input.ExpectedRevision, ct);
        return (await GetAsync(row.Id, ct))!;
    }

    private async Task<bool> EmptySharedDownloadDirectoryAsync(string path, Func<string, Task>? guardDirectory)
    {
        try
        {
            if (guardDirectory != null) await guardDirectory(path);
            storage.EnsurePathAllowed(path);
            ArtifactManifestFiles.RejectLinks(path);
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (DirectoryNotFoundException) {return !File.Exists(path);}
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) {return false;}
    }

    // Ignore only identifiers, display names and producer evidence that legitimately differ
    // between groups. Keep the complete locator, credentials and executable instructions.
    private static string SharedDownloadInstructions(AcquisitionPlanPart part) => Serialize(new
    {
        part.SelectionMode, part.Processing, part.StoragePolicy, part.UseAdaptiveAssessment,
        HasAppliedProcessingRevision = part.AppliedProcessingRevisionId != null,
        Sources = part.Sources.Select(source => new
        {
            Selected = source.Id == part.SelectedSourceId,
            source.Url, source.Kind, source.ProviderId, source.MethodId, source.AccountId,
            source.AccessCode, source.ArchivePassword, source.Processing, source.ProcessingIssueCode,
            source.OriginalProcessingPlanJson, source.SourceCorrection, source.ManualAvailabilityOverride,
            HasAppliedProcessingRevision = source.AppliedProcessingRevisionId != null
        })
    });
}
