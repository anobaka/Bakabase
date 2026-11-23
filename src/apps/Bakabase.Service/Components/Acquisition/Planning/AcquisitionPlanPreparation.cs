using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    /// <summary>Inspects preparation without changing the task, parser, execution or source selection.</summary>
    public async Task<AcquisitionPreparationPreview> PreviewPreparationAsync(int id, AcquisitionPreparationInput input, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await PreparationEditableAsync(id, input.ExpectedRevision, ct);
            var snapshot = await ReadPreparationAsync(row, input, ct);
            return await CheckPreparationAsync(row, input, snapshot, input.CheckAvailability != false, ct);
        }
        finally {gate.Release();}
    }

    /// <summary>Freezes the reviewed sources and instructions. Starting a download is a separate action.</summary>
    public async Task<AcquisitionPlanTaskDetail> ConfirmPreparationAsync(int id, AcquisitionPreparationInput input, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await PreparationEditableAsync(id, input.ExpectedRevision, ct);
            var snapshot = await ReadPreparationAsync(row, input, ct);
            var preview = await CheckPreparationAsync(row, input, snapshot, false, ct);
            if (!preview.CanConfirm)
                throw new InvalidOperationException(string.Join(" ", preview.Requirements.Where(r => r.Code != "OUTPUT_DIRECTORY_REQUIRED")
                    .Select(r => r.Code + ": " + r.Message).DefaultIfEmpty("Select ready sources before confirming preparation.")));
            var checkedById = preview.Groups.SelectMany(g => g.Sources).ToDictionary(c => c.Candidate.Id, StringComparer.Ordinal);
            var parts = new List<AcquisitionPlanPart>();
            foreach (var group in preview.Groups)
            {
                if (group.SelectedSourceIds.Count == 0) continue;
                var selections = group.SelectionMode == "single" ? new[] {group.SelectedSourceIds} :
                    group.SelectedSourceIds.Select(id => (IReadOnlyList<string>)new[] {id}).ToArray();
                foreach (var selection in selections)
                {
                    var selectedId = selection.Single();
                    var previous = snapshot.Plan?.Parts.SingleOrDefault(p => p.Id == group.Id);
                    var members = group.SelectionMode == "single" ? group.Sources :
                        group.Sources.Where(c => c.Candidate.Id == selectedId).ToArray();
                    var sources = members.Select(c => FreezePreparationSource(snapshot.Sources.Single(s => s.Id == c.Candidate.Id),
                        snapshot.Evidence.GetValueOrDefault(c.Candidate.Id))).Select(source => source with
                    {
                        ManualAvailabilityOverride = source.Id == selectedId && preview.AllowUnavailableSourceIds.Contains(source.Id)
                            ? AcquisitionSourceAvailabilityOverrides.Create(row.Id, source, checkedById[source.Id].ResolvedSource!) : null
                    }).ToList();
                    var selected = sources.Single(s => s.Id == selectedId);
                    var sameSelected = previous?.SelectedSourceId == selectedId && snapshot.Evidence.TryGetValue(selectedId, out var old) &&
                        SamePreparationInstructions(old.Processing, selected.Processing) && SamePreparationLocator(old, selected);
                    var unchangedSelection = sameSelected && snapshot.Evidence.TryGetValue(selectedId, out var originalSelection) &&
                        originalSelection.AccessCode == selected.AccessCode && originalSelection.ArchivePassword == selected.ArchivePassword &&
                        originalSelection.MethodId == selected.MethodId && originalSelection.AccountId == selected.AccountId;
                    var part = (previous ?? new AcquisitionPlanPart
                    {
                        Id = group.SelectionMode == "single" ? group.Id : PreparationPartId(selectedId),
                        Title = checkedById[selectedId].Candidate.Title
                    }) with
                    {
                        Sources = sources, SelectedSourceId = selectedId,
                        SelectionMode = preview.AllowUnavailableSourceIds.Contains(selectedId) ? "manual" : unchangedSelection ? previous!.SelectionMode : "manual",
                        Processing = sameSelected ? previous!.Processing : selected.Processing,
                        ProcessingSource = sameSelected ? previous!.ProcessingSource : selected.ProcessingSource,
                        AppliedProcessingRevisionId = sameSelected ? previous!.AppliedProcessingRevisionId : selected.AppliedProcessingRevisionId
                    };
                    parts.Add(part);
                }
            }
            var plan = (snapshot.Plan ?? new AcquisitionPlan {Title = Title(row.Title ?? ""), OutputDirectory = row.TargetDirectory ?? ""}) with
            {
                // SavePlan reuses recorded bindings and allocates any newly selected independent parts.
                DeliveryLayout = null, StageDirectories = null,
                Parts = parts, SourceProviderPriority = snapshot.Plan?.SourceProviderPriority?.Count > 0
                    ? snapshot.Plan.SourceProviderPriority : snapshot.Priority.ToList()
            };
            AcquisitionPlanCompiler.Validate(plan);
            ValidateDirectory(plan.OutputDirectory, allowEmpty: true);
            var issues = preview.Requirements.Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList();
            var groupBySource = parts.SelectMany(p => p.Sources.Select(s => (s.Id, GroupId: p.Id))).ToDictionary(s => s.Id, s => s.GroupId, StringComparer.Ordinal);
            var state = State(row);
            AcquisitionStageDirectories? directoryBindings = null;
            if (snapshot.Plan?.Parts.Count == 1 && parts.Count > 1)
            {
                // A single part owns the whole task folder. Splitting this unstarted, empty
                // reservation into parts must allocate siblings instead of overlapping folders.
                // Previously executed tasks and any local payloads were rejected above.
                if (snapshot.Plan.StageDirectories?.Parts.Values.Any(p => new[] {p.Download, p.Processing, p.Delivery}
                        .Any(d => d?.Ownership == "borrowed")) == true)
                    throw new InvalidOperationException("SOURCE_REPLACEMENT_LOCAL_FILES: Keep the task's borrowed stage directories.");
                state = state with {DeliveryLayout = state.DeliveryLayout is { } layout ? layout with {PartDirectories = []} : null};
                if (Path.IsPathFullyQualified(plan.OutputDirectory)) directoryBindings = new() {TaskId = row.Id, Parts = []};
            }
            row.BuilderStateJson = Serialize(state with
            {
                DraftSources = snapshot.Sources.Select(s => s with {GroupId = groupBySource.GetValueOrDefault(s.Id)}).ToList(),
                Candidates = [], Issues = issues
            });
            SavePlan(row, plan, Sources(row), reason: "Source preparation confirmed", refreshStageDirectories: true,
                currentDirectoryBindings: directoryBindings);
            row.BuilderId = "prepared";
            if (snapshot.Plan != null && HasOwnedStageFiles(snapshot.Plan))
                throw new InvalidOperationException("SOURCE_REPLACEMENT_LOCAL_FILES");
            await SaveDraftEditAsync(row, input.ExpectedRevision, ct);
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }

    private async Task<AcquisitionTaskDbModel> PreparationEditableAsync(int id, int expectedRevision, CancellationToken ct)
    {
        var row = await Editable(id, ct); Revision(row, expectedRevision);
        if (!await HasNeverExecutedAsync(row, ct))
            throw new InvalidOperationException("PLAN_ALREADY_STARTED: Preparation cannot change a recorded execution.");
        return row;
    }

    private sealed record PreparationSnapshot(AcquisitionPlan? Plan, IReadOnlyList<AcquisitionDraftSource> Sources,
        IReadOnlyDictionary<string, AcquisitionPlanSource> Evidence, IReadOnlyDictionary<string, string?> KnownGroups,
        IReadOnlyList<string> Priority);

    private async Task<PreparationSnapshot> ReadPreparationAsync(AcquisitionTaskDbModel row, AcquisitionPreparationInput input, CancellationToken ct)
    {
        if (input.Relationship is not (null or "mirrors" or "independent") || input.SelectedSourceIds?.Count > 100 ||
            input.SelectedSourceIds?.Distinct(StringComparer.Ordinal).Count() != input.SelectedSourceIds?.Count ||
            input.AllowUnavailableSourceIds?.Count > 100 ||
            input.AllowUnavailableSourceIds?.Distinct(StringComparer.Ordinal).Count() != input.AllowUnavailableSourceIds?.Count)
            throw new ArgumentException("PREPARATION_SELECTION_INVALID: Supply a valid source relationship and unique source selections.");
        var plan = row.PlanJson == null ? null : await BindProcessingSourcesAsync(Plan(row), Sources(row), ct);
        var evidence = new Dictionary<string, AcquisitionPlanSource>(StringComparer.Ordinal);
        var groups = new Dictionary<string, string?>(StringComparer.Ordinal);
        var baseline = new List<AcquisitionDraftSource>();
        foreach (var part in plan?.Parts ?? [])
        {
            if (part.ContinueFrom != null) throw new InvalidOperationException("PLAN_ALREADY_STARTED: Keep the recorded continuation plan.");
            groups.Add(part.Id, part.Title);
            foreach (var source in part.Sources)
            {
                // The selected part may contain a later reviewed processing revision than its original source entry.
                var effective = source.Id == part.SelectedSourceId ? source with
                {
                    Processing = part.Processing ?? source.Processing, ProcessingSource = part.ProcessingSource ?? source.ProcessingSource,
                    AppliedProcessingRevisionId = part.AppliedProcessingRevisionId ?? source.AppliedProcessingRevisionId
                } : source;
                evidence.Add(source.Id, effective);
                baseline.Add(DraftSource(effective, part.Id, part.Title));
            }
        }
        baseline.AddRange(State(row).DraftSources.Where(s => baseline.All(existing => existing.Id != s.Id)));
        if (plan == null) await BindDraftPreparationSourcesAsync(row, baseline, evidence, groups, ct);
        var maximumSources = plan == null ? 100 : 500;
        var supplied = input.Sources?.ToList() ?? baseline;
        if (supplied.Count > maximumSources || supplied.Any(s => s == null) ||
            supplied.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != supplied.Count)
            throw new ArgumentException($"Supply at most {maximumSources} uniquely identified acquisition sources.");
        if (input.Sources != null)
        {
            // An unsupported legacy alternative is retained evidence. Round-tripping an
            // unchanged card must not make an unrelated valid source impossible to edit.
            var changed = supplied.Where(s => !baseline.Any(original => original.Id == s.Id &&
                Serialize(s with {GroupId = s.GroupId ?? original.GroupId}) == Serialize(original))).ToList();
            ValidateDraftSources(changed, maximumSources, plan == null ? 2048 : 8192);
        }
        // Editing a card never deletes its unselected siblings or their source evidence.
        var sources = supplied.Concat(baseline.Where(s => supplied.All(changed => changed.Id != s.Id))).Select(source =>
        {
            var original = baseline.SingleOrDefault(s => s.Id == source.Id);
            if (original?.GroupId is { } originalGroup && groups.ContainsKey(originalGroup))
            {
                if (source.GroupId != null && source.GroupId != originalGroup)
                    throw new ArgumentException("PREPARATION_GROUP_CHANGED: Keep existing required content groups separate.");
                return source with {GroupId = originalGroup};
            }
            if (source.GroupId != null && !groups.ContainsKey(source.GroupId))
                throw new ArgumentException("PREPARATION_GROUP_INVALID: Add a mirror to an existing content group.");
            return source;
        }).ToList();
        if (sources.Count > maximumSources || sources.Where(s => s.GroupId != null).GroupBy(s => s.GroupId).Any(g => g.Count() > 100))
            throw new ArgumentException($"Supply at most {maximumSources} acquisition sources and at most 100 mirrors per content group.");
        var priority = acquisitionOptions?.Value.SourceProviderPriority ?? new AcquisitionOptions().SourceProviderPriority;
        return new(plan, sources, evidence, groups, priority.ToList());
    }

    private async Task BindDraftPreparationSourcesAsync(AcquisitionTaskDbModel row, List<AcquisitionDraftSource> sources,
        Dictionary<string, AcquisitionPlanSource> evidence, Dictionary<string, string?> groups, CancellationToken ct)
    {
        var refs = Sources(row).Where(r => r.Module == "postParser" && r.EntityKind == "task" && r.ResultKey != null && r.Revision != null).Distinct().ToList();
        foreach (var origin in refs.GroupBy(r => (r.EntityId, r.Revision)))
        {
            if (!int.TryParse(origin.Key.EntityId, out var taskId) || !int.TryParse(origin.Key.Revision, out var revision)) continue;
            var post = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.Revision == revision && !t.IsDeleted, ct);
            if (post == null || !PostParserSavedResults.HasCompletedDownloadResult(post)) continue;
            try {await PostProcessingPlanSourceReader.EnsureStableAsync(db, post, ct);}
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) {continue;}
            var info = PostParserSavedResults.ReadDownloadInfo(post);
            var declared = (info.Groups ?? []).Where(g => g != null).GroupBy(g => g.Id)
                .Where(g => g.Count() == 1 && g.Single().Kind == "main").ToDictionary(g => g.Key, g => g.Single());
            var effective = processingPlans == null ? [] : await processingPlans.ResolveAsync(PostProcessingPlanSourceReader.Snapshots(post), ct);
            foreach (var reference in origin)
            {
                var position = sources.FindIndex(s => s.Id == reference.ResultKey);
                if (position < 0 || refs.Count(r => r.ResultKey == reference.ResultKey) != 1 ||
                    !int.TryParse(reference.ResultKey, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= info.Resources.Count) continue;
                var source = sources[position]; var original = info.Resources[index];
                if (source.Url != original.Link || source.AccessCode != original.Code || source.ArchivePassword != original.Password) continue;
                var processing = effective.SingleOrDefault(p => p.Source == reference);
                var sameInstructions = SamePreparationInstructions(source.Processing, processing?.EffectivePlan ?? original.Extraction);
                evidence[source.Id] = new()
                {
                    Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId,
                    MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode,
                    ArchivePassword = source.ArchivePassword, Processing = source.Processing,
                    ProcessingSource = reference, AppliedProcessingRevisionId = sameInstructions && processing?.EffectiveRevision > 0 ? processing.History.First().Id : null
                };
                if (original.GroupId is { } group && declared.TryGetValue(group, out var declaration))
                {
                    var groupId = "post-group-" + CloudOperationService.Key($"{taskId}:{revision}:{group}")[..20];
                    groups[groupId] = declaration.Title;
                    sources[position] = source with {GroupId = groupId};
                }
            }
        }
    }

    private async Task<AcquisitionPreparationPreview> CheckPreparationAsync(AcquisitionTaskDbModel row, AcquisitionPreparationInput input,
        PreparationSnapshot snapshot, bool checkAvailability, CancellationToken ct)
    {
        var ungrouped = snapshot.Sources.Where(s => s.GroupId == null).ToList();
        var requiresRelationship = snapshot.Plan == null && ungrouped.Count > 1;
        var relationship = requiresRelationship ? input.Relationship : null;
        var requirements = new List<DownloadRequirement>();
        if (snapshot.Plan != null && HasOwnedStageFiles(snapshot.Plan))
            requirements.Add(new("SOURCE_REPLACEMENT_LOCAL_FILES", "source", "This task already has local files. Continue its download stage before changing sources."));
        if (requiresRelationship && relationship == null)
            requirements.Add(new("SOURCE_RELATIONSHIP_REQUIRED", "source", "Choose whether these links are alternatives or separate downloads."));
        var candidates = snapshot.Sources.Select(s => new DownloadCandidate
        {
            Id = s.Id, Title = s.Title, Url = s.Url, Kind = s.Kind, ProviderId = s.ProviderId,
            GroupId = s.GroupId, MirrorGroupConfirmed = s.GroupId != null,
            AccessCode = s.AccessCode, ArchivePassword = s.ArchivePassword, MethodId = s.MethodId, AccountId = s.AccountId,
            ExtractionPlanJson = snapshot.Evidence.GetValueOrDefault(s.Id) is {ProcessingIssueCode: not null} original &&
                SamePreparationInstructions(original.Processing, s.Processing) ? original.OriginalProcessingPlanJson : s.Processing == null ? null : Serialize(s.Processing)
        }).ToList();
        var check = candidates.Count == 0 ? new DownloadIntentPreflight("files", [], [], false) :
            await downloads.PreflightAsync(new()
            {
                Mode = "files", Title = row.Title, Candidates = candidates, AvailabilityContextKey = $"acquisition:{row.Id}",
                CheckSourceAvailability = checkAvailability
            }, snapshot.Plan?.OutputDirectory ?? row.TargetDirectory, ct);
        check = check with {Candidates = check.Candidates.Select(candidate =>
            snapshot.Evidence.GetValueOrDefault(candidate.Candidate.Id) is {ProcessingIssueCode: not null} original &&
            SamePreparationInstructions(original.Processing, snapshot.Sources.Single(s => s.Id == candidate.Candidate.Id).Processing)
                ? candidate with {CanSelect = false, CanSelectManually = false, State = "needsSetup", Requirements = candidate.Requirements.Append(new DownloadRequirement(
                    "SOURCE_PROCESSING_INVALID", "processing", "Review this source's retained processing instructions before selecting it.")).ToList()}
                : candidate).ToList()};
        requirements.AddRange(check.Requirements);
        // Preview always retains the actual availability and normal eligibility. The user's
        // explicit choice affects confirmation only, never recommendations or shared cache.
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in check.Candidates)
        {
            var source = snapshot.Sources.Single(s => s.Id == candidate.Candidate.Id);
            var previous = snapshot.Evidence.GetValueOrDefault(source.Id);
            var requestedOverride = input.AllowUnavailableSourceIds?.Contains(source.Id) ??
                previous != null && AcquisitionSourceAvailabilityOverrides.Matches(
                    FreezePreparationSource(source, previous), row.Id, candidate.ResolvedSource);
            if (requestedOverride && candidate.CanSelectManually == true && candidate.ResolvedSource != null &&
                SourceAvailability.SourceAvailabilityService.IsConfirmedUnavailable(candidate.Availability)) allowed.Add(source.Id);
        }
        var requested = input.SelectedSourceIds?.ToHashSet(StringComparer.Ordinal);
        if (requested != null && requested.Except(candidates.Select(c => c.Id), StringComparer.Ordinal).Any())
            requirements.Add(new("SELECTION_INVALID", "source", "Select sources from this preparation preview."));
        var groups = new List<AcquisitionPreparationGroup>();
        void AddGroup(string id, string? title, bool required, string mode, IReadOnlyList<DownloadCandidateView> members)
        {
            if (members.Count == 0) return;
            var ready = members.Where(c => c.CanSelect).ToList();
            var recommended = mode == "single" ? ready.Count == 0 ? [] : new[] {AcquisitionSourceSelectionPolicy.Choose(ready, snapshot.Priority).Candidate.Id} :
                ready.Select(c => c.Candidate.Id).ToArray();
            var prior = snapshot.Plan?.Parts.SingleOrDefault(p => p.Id == id);
            IReadOnlyList<string> selected = requested != null ? members.Where(c => requested.Contains(c.Candidate.Id)).Select(c => c.Candidate.Id).ToList() :
                prior?.SelectedSourceId is { } saved ? new[] {saved} : required ? recommended : [];
            if (required && selected.Count == 0 || mode == "single" && selected.Count > 1)
                requirements.Add(new("SOURCE_SELECTION_REQUIRED", "source", "Choose one source for each required content group."));
            requirements.AddRange(members.Where(c => selected.Contains(c.Candidate.Id)).SelectMany(c => c.Requirements
                .Where(r => r.Code != "SOURCE_CONFIRMED_UNAVAILABLE" || !allowed.Contains(c.Candidate.Id))));
            groups.Add(new(id, title, required, mode, selected, recommended, members));
        }
        foreach (var (id, title) in snapshot.KnownGroups)
            AddGroup(id, title, true, "single", check.Candidates.Where(c => c.Candidate.GroupId == id).ToList());
        if (ungrouped.Count > 0)
        {
            var independent = snapshot.Plan != null || relationship == "independent";
            AddGroup(snapshot.Plan != null ? "additional-sources" : independent ? "independent-sources" : "main-sources", null,
                snapshot.Plan == null, independent ? "multiple" : "single", check.Candidates.Where(c => c.Candidate.GroupId == null).ToList());
        }
        var selectedIds = groups.SelectMany(g => g.SelectedSourceIds).ToHashSet(StringComparer.Ordinal);
        allowed.IntersectWith(selectedIds);
        if (input.AllowUnavailableSourceIds?.Any(id => !selectedIds.Contains(id)) == true)
            requirements.Add(new("SELECTION_INVALID", "source", "Only explicitly selected sources may override an unavailable observation."));
        if (selectedIds.Count == 0)
            requirements.Add(new(candidates.Count == 0 ? "DOWNLOAD_INFO_MISSING" : "SOURCE_SELECTION_REQUIRED", "source", "Choose at least one download source."));
        var canConfirm = selectedIds.Count > 0 && requirements.All(r => r.Code == "OUTPUT_DIRECTORY_REQUIRED") &&
            check.Candidates.Where(c => selectedIds.Contains(c.Candidate.Id)).All(c => c.CanSelect ||
                c.CanSelectManually == true && allowed.Contains(c.Candidate.Id));
        return new(row.PlanRevision, row.Title ?? "", snapshot.Priority, requiresRelationship, relationship, groups,
            requirements.DistinctBy(r => (r.Code, r.Message)).ToList(), canConfirm, allowed.ToArray());
    }

    private static string PreparationPartId(string sourceId) => "part-" + CloudOperationService.Key("source:" + sourceId)[..20];
    private static AcquisitionDraftSource DraftSource(AcquisitionPlanSource source, string? groupId, string? title) => new()
    {
        Id = source.Id, Title = title, GroupId = groupId, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId,
        MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode,
        ArchivePassword = source.ArchivePassword, Processing = source.Processing
    };
    private static bool SamePreparationInstructions(FileProcessingPlan? first, FileProcessingPlan? second) => Serialize(first) == Serialize(second);
    private static bool SamePreparationLocator(AcquisitionPlanSource first, AcquisitionPlanSource second) =>
        first.Url == second.Url && first.Kind == second.Kind && first.ProviderId == second.ProviderId;

    private static AcquisitionPlanSource FreezePreparationSource(AcquisitionDraftSource source, AcquisitionPlanSource? previous)
    {
        var frozen = (previous ?? new AcquisitionPlanSource()) with
        {
            Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId,
            MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode,
            ArchivePassword = source.ArchivePassword, Processing = source.Processing
        };
        var sameLocator = previous != null && SamePreparationLocator(previous, frozen);
        var sameInstructions = previous != null && SamePreparationInstructions(previous.Processing, source.Processing);
        if (!sameLocator) frozen = frozen with
        {
            OriginalUrl = null, ResolutionChain = null, ProcessingSource = null,
            AppliedProcessingRevisionId = null, SourceCorrection = null
        };
        if (!sameInstructions) frozen = frozen with {AppliedProcessingRevisionId = null, OriginalProcessingPlanJson = null, ProcessingIssueCode = null};
        if (previous?.AccessCode != source.AccessCode) frozen = frozen with {SourceCorrection = null};
        // Resolution metadata can explain a URL, but preparation never changes the user's locator silently.
        return frozen;
    }
}
