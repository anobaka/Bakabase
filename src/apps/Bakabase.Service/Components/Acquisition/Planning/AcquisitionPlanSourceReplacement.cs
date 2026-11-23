using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionPlanService
{
    private async Task<AcquisitionPlanTaskDetail> ReplaceUnstartedSourceAsync(AcquisitionTaskDbModel row,
        int revision, AcquisitionPlanSourceReplacement input, CancellationToken ct)
    {
        if (row.PlanJson == null || !await HasNeverExecutedAsync(row, ct))
            throw new InvalidOperationException("PLAN_ALREADY_STARTED");
        var plan = Plan(row);
        if (plan.Parts.Any(p => p.ContinueFrom != null)) throw new InvalidOperationException("PLAN_ALREADY_STARTED");
        if (HasOwnedStageFiles(plan)) throw new InvalidOperationException("SOURCE_REPLACEMENT_LOCAL_FILES");
        var part = plan.Parts.SingleOrDefault(p => p.Id == input.PartId);
        var selected = part?.Sources.SingleOrDefault(s => s.Id == input.SourceId && part.SelectedSourceId == s.Id);
        if (part == null || selected == null) throw new InvalidOperationException("PLAN_VERSION_CONFLICT");
        if (input.Url is not {Length: > 0 and <= 2048} || !Uri.TryCreate(input.Url, UriKind.Absolute, out var url) ||
            url.Scheme is not ("https" or "http") || url.UserInfo.Length > 0 || input.AccessCode?.Length > 512)
            throw new ArgumentException("SOURCE_REPLACEMENT_INVALID");
        if (part.Sources.Any(s => s.Url == input.Url)) throw new ArgumentException("SOURCE_REPLACEMENT_DUPLICATE");

        var reviewed = selected with {Id = Guid.NewGuid().ToString("N"), Url = input.Url, Kind = "auto",
            AccessCode = input.AccessCode ?? selected.AccessCode, AccountId = null, MethodId = null, ProviderId = null,
            OriginalUrl = null, ResolutionChain = null, SourceCorrection = null, ManualAvailabilityOverride = null};
        var revisedPart = part with {Sources = [..part.Sources, reviewed], SelectedSourceId = reviewed.Id, SelectionMode = "manual"};
        var revisedPlan = plan with {Parts = plan.Parts.Select(p => p.Id == part.Id ? revisedPart : p).ToList()};
        AcquisitionPlanCompiler.Validate(revisedPlan);
        var preflight = await downloads.PreflightAsync(Request(revisedPlan, $"acquisition:{row.Id}"), plan.OutputDirectory, ct);
        var candidate = preflight.Candidates.SingleOrDefault(c => c.Candidate.Id == reviewed.Id);
        if (candidate?.ResolvedSource is not { } resolved || candidate.SelectedMethodId is null or "manual" ||
            !candidate.Methods.Any(m => m.Id == candidate.SelectedMethodId && m.Supported))
            throw new ArgumentException("SOURCE_REPLACEMENT_UNSUPPORTED");
        if (part.Sources.Any(s => s.Url == resolved.Url)) throw new ArgumentException("SOURCE_REPLACEMENT_DUPLICATE");

        revisedPart.Sources[^1] = reviewed with {Url = resolved.Url, Kind = resolved.Kind, ProviderId = resolved.ProviderId,
            AccountId = resolved.AccountId, AccessCode = resolved.AccessCode, MethodId = candidate.SelectedMethodId,
            OriginalUrl = resolved.OriginalUrl, ResolutionChain = resolved.ResolutionChain.ToList()};
        var issues = preflight.Requirements.Concat(preflight.Candidates.SelectMany(c => c.Requirements))
            .Select(r => new AcquisitionPlanIssue(r.Code, r.Message)).DistinctBy(i => i.Code).ToList();
        var processingIssues = await FileProcessingDependencyPreflight.InspectAsync(
            revisedPlan.Parts.Select(AcquisitionPlanCompiler.Processing), processingDependencies ?? [], ct);
        issues.AddRange(processingIssues.Select(i => new AcquisitionPlanIssue(i.Code, i.Message)));
        row.BuilderStateJson = Serialize(State(row) with {Issues = issues});
        SavePlan(row, revisedPlan, Sources(row));
        if (HasOwnedStageFiles(plan)) throw new InvalidOperationException("SOURCE_REPLACEMENT_LOCAL_FILES");
        await SaveDraftEditAsync(row, revision, ct);
        return (await GetAsync(row.Id, ct))!;
    }

    private bool HasOwnedStageFiles(AcquisitionPlan plan)
    {
        foreach (var part in plan.StageDirectories?.Parts.Values.AsEnumerable() ?? [])
        foreach (var location in new[] {part.Download, part.Processing, part.Delivery})
        {
            if (location?.Ownership != "exclusive") continue;
            storage.EnsurePathAllowed(location.Directory);
            try
            {
                if (Directory.EnumerateFileSystemEntries(location.Directory).Any()) return true;
            }
            catch (DirectoryNotFoundException) { /* An unstarted task normally has no stage folders yet. */ }
        }
        return false;
    }
}
