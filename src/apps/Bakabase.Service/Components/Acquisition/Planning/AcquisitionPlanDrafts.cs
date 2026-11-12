using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    /// <summary>Records intent only. Neither creating a draft nor adding a locator searches or executes it.</summary>
    public async Task<AcquisitionPlanTaskDetail> CreateDraftAsync(AcquisitionDraftCreationInput input, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await CreateDraftRowAsync(input, null, [], [], null, ct);
        await transaction.CommitAsync(ct);
        return await DetailAsync(row, ct);
    }

    internal async Task<AcquisitionTaskDbModel> CreateDraftRowAsync(AcquisitionDraftCreationInput input,
        int? resourceId, IReadOnlyList<AcquisitionLegacyLeadSnapshot> legacyLeads,
        IReadOnlyList<WorkflowSourceRef> sourceRefs, string? importKey, CancellationToken ct,
        string? legacyTitle = null, AcquisitionDraftListRow? importedListRow = null)
    {
        input = input with {OutputDirectory = outputDirectories?.ResolveNew(input.OutputDirectory) ?? input.OutputDirectory};
        var deliverySettings = DeliveryDirectories.Capture(input.DeliverySettings);
        var sources = ValidateDraftSources(input.Sources ?? []);
        ValidateDirectory(input.OutputDirectory, allowEmpty: true);
        var title = string.IsNullOrWhiteSpace(input.Title)
            ? sources.FirstOrDefault()?.Title ?? (sources.Count > 0 ? "Acquisition" : null) : input.Title;
        var row = new AcquisitionTaskDbModel
        {
            Title = Title(title ?? ""), BuilderId = "draft", Phase = "preparing",
            Status = AcquisitionStatus.Waiting, TargetDirectory = input.OutputDirectory,
            CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        };
        Tasks.Add(row);
        await db.SaveChangesAsync(ct);
        var refs = sourceRefs.Append(Owner(row)).Distinct().ToList();
        row.SourceRefsJson = Serialize(refs);
        row.BuilderStateJson = Serialize(new AcquisitionPlanBuilderState
        {
            DeliverySettings = deliverySettings, StoragePolicy = CaptureStorage(input.StoragePolicy), Query = row.Title, DraftSources = sources, LegacyLeads = legacyLeads.ToList(),
            LegacyResourceId = resourceId, SourceRefs = refs, ImportKey = importKey,
            CreationOrigins = creationOrigins == null ? [] : await creationOrigins.CaptureAsync(sourceRefs, "draft",
                resourceId == null && importKey == null, ct),
            LegacyTitle = legacyTitle, ImportedListRow = importedListRow
        });
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<AcquisitionPlanTaskDetail> UpdateDraftAsync(int id, AcquisitionDraftUpdateInput input, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            if (row.PlanJson != null) throw new InvalidOperationException("PLAN_ALREADY_PREPARED: Replace the prepared plan explicitly.");
            await UpdateDeliverySettingsAsync(row, input.DeliverySettings, ct);
            await UpdateStoragePolicyAsync(row, input.StoragePolicy, null, ct);
            var state = State(row);
            if (input.Title != null) row.Title = Title(input.Title);
            if (input.Sources != null) state = state with {DraftSources = ValidateDraftSources(input.Sources)};
            if (input.OutputDirectory != null)
            { ValidateDirectory(input.OutputDirectory, true); row.TargetDirectory = input.OutputDirectory; }
            row.BuilderStateJson = Serialize(state with {Issues = []});
            row.Phase = "preparing"; row.PlanRevision++; row.UpdatedAt = DateTime.Now;
            await SaveDraftEditAsync(row, input.ExpectedRevision, ct);
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> SetDraftBuilderAsync(int id, AcquisitionDraftBuilderInput input, CancellationToken ct)
    {
        if (!builders.Any(b => b.Descriptor.Id == input.BuilderId && b.Descriptor.EntryPoints.Contains("manual")))
            throw new ArgumentException("Choose an available manual acquisition builder.");
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            row.BuilderId = input.BuilderId; row.Phase = "preparing"; row.PlanJson = null;
            row.BuilderStateJson = Serialize(State(row) with {Candidates = [], Issues = []});
            row.PlanRevision++; row.UpdatedAt = DateTime.Now;
            await SaveDraftEditAsync(row, input.ExpectedRevision, ct);
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }

    public async Task<AcquisitionPlanTaskDetail> PrepareDraftAsync(int id, AcquisitionDraftPreparationInput input, CancellationToken ct)
    {
        if (input.Plan == null) throw new ArgumentException("Supply an acquisition plan.");
        var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct);
        try
        {
            var row = await Editable(id, ct); Revision(row, input.ExpectedRevision);
            ValidateDirectory(input.Plan.OutputDirectory, allowEmpty: true);
            // The owner may explicitly supply instructions, but only producer adapters bind source revisions.
            var plan = input.Plan with {DeliveryLayout = null, Title = Title(input.Plan.Title), Parts = input.Plan.Parts.Select(p => p with
                {ProcessingSource = null, AppliedProcessingRevisionId = null, SelectionMode = "manual",
                    Sources = p.Sources.Select(s => s with {ProcessingSource = null, AppliedProcessingRevisionId = null}).ToList()}).ToList()};
            AcquisitionPlanCompiler.Validate(plan);
            var check = await downloads.PreflightAsync(Request(plan, $"acquisition:{row.Id}") with
                {IgnoreSourceAvailability = !await HasNeverExecutedAsync(row, ct)}, plan.OutputDirectory, ct);
            var issues = check.Requirements.Concat(check.Candidates.SelectMany(c => c.Requirements))
                .Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList();
            SavePlan(row, plan, Sources(row), refreshStageDirectories: true);
            row.BuilderId = "prepared";
            row.Phase = issues.Any(i => i.Blocking && i.Code != "OUTPUT_DIRECTORY_REQUIRED") ? "attention" : "ready";
            row.BuilderStateJson = Serialize(State(row) with {Issues = issues});
            await SaveDraftEditAsync(row, input.ExpectedRevision, ct);
            return (await GetAsync(id, ct))!;
        }
        finally {gate.Release();}
    }

    private async Task SaveDraftEditAsync(AcquisitionTaskDbModel row, int expectedRevision, CancellationToken ct)
    {
        var changed = await Tasks.Where(t => t.Id == row.Id && t.DeletedAt == null && t.PlanRevision == expectedRevision &&
            t.WorkflowRunId == null && t.Status != AcquisitionStatus.Cancelled).ExecuteUpdateAsync(update => update
            .SetProperty(t => t.Title, row.Title).SetProperty(t => t.BuilderId, row.BuilderId)
            .SetProperty(t => t.Phase, row.Phase).SetProperty(t => t.PlanRevision, row.PlanRevision)
            .SetProperty(t => t.PlanJson, row.PlanJson).SetProperty(t => t.PlanHistoryJson, row.PlanHistoryJson)
            .SetProperty(t => t.BuilderStateJson, row.BuilderStateJson).SetProperty(t => t.TargetDirectory, row.TargetDirectory)
            .SetProperty(t => t.UpdatedAt, row.UpdatedAt), ct);
        db.Entry(row).State = EntityState.Detached;
        if (changed != 1) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
    }

    private static List<AcquisitionDraftSource> ValidateDraftSources(IReadOnlyList<AcquisitionDraftSource> sources)
    {
        if (sources.Count > 100 || sources.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id) ||
            s.Id.Length > 128 || string.IsNullOrWhiteSpace(s.Url) || s.Url.Length > 2048 || s.Title?.Length > 512 ||
            s.AccessCode?.Length > 512 || s.ArchivePassword?.Length > 2048 || s.ProviderId?.Length > 128 ||
            s.MethodId?.Length > 128 || s.AccountId?.Length > 128 ||
            s.Kind is not ("auto" or "http" or "share" or "remote" or "torrent")) ||
            sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new ArgumentException("Supply at most 100 valid, uniquely identified acquisition sources.");
        foreach (var source in sources)
        {
            if (source.Kind == "remote")
            {
                if (string.IsNullOrWhiteSpace(source.ProviderId)) throw new ArgumentException("A remote locator needs its provider.");
            }
            else if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
                     uri.Scheme is not ("http" or "https" or "magnet") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Supply a valid source URL without embedded credentials.");
            if (source.Processing != null) FileProcessingPlanValidator.Validate(source.Processing);
        }
        return sources.ToList();
    }
}
