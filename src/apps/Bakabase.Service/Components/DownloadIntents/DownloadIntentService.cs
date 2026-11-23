using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.Acquisition.Planning;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.DownloadIntents;

public sealed class DownloadIntentService(DownloadMethodRegistry registry, DownloadIntentStarter starter,
    IUserStoragePolicy storage, BakabaseDbContext db, IPlaceholderResourceService placeholders,
    IResourceService resources, IAcquisitionLeadService leads, IAcquisitionService acquisitions,
    IWorkflowDefinitionService workflows, IWorkflowActivityRegistry? activities = null, SourceAvailabilityService? availability = null)
{
    public const string AcquisitionRecipeName = "Acquire supported sources (v1)";
    private static readonly SemaphoreSlim RecipeGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public Task<DownloadIntentPreflight> PreflightDownloadsOnlyAsync(DownloadIntentRequest input,
        string? outputDirectory, CancellationToken ct)
    {
        ValidateDownloadsOnlyInput(input);
        return PreflightAsync(input, outputDirectory, ct);
    }

    public Task<DownloadIntentSubmission> SubmitDownloadsOnlyAsync(DownloadIntentRequest input,
        string? outputDirectory, CancellationToken ct)
    {
        ValidateDownloadsOnlyInput(input);
        return SubmitAsync(input, outputDirectory, [], null, ct, downloadsOnly: true);
    }

    private static void ValidateDownloadsOnlyInput(DownloadIntentRequest input)
    {
        if (input.Mode != "files" || input.WorkflowDefinitionId != null)
            throw new ArgumentException("The download center only creates downloads. Configure subsequent operations in a workflow.");
    }

    public async Task<DownloadIntentPreflight> PreflightAsync(DownloadIntentRequest input,
        string? outputDirectory, CancellationToken ct)
    {
        ValidateInput(input);
        var requirements = new List<DownloadRequirement>();
        if (input.Mode != "lead")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outputDirectory) || !Path.IsPathFullyQualified(outputDirectory))
                    throw new ArgumentException("Choose an absolute output directory.");
                storage.EnsurePathAllowed(outputDirectory);
            }
            catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
            {requirements.Add(new("OUTPUT_DIRECTORY_REQUIRED", "configuration", error.Message, Actions: ["configure"]));}
        }
        if (input.Mode is "lead" or "library" && string.IsNullOrWhiteSpace(input.Title))
            requirements.Add(new("TITLE_REQUIRED", "configuration", "Enter a resource title.", Actions: ["configure"]));
        if (input.Mode != "lead" && input.WorkflowDefinitionId != null)
        {
            try { await ValidateWorkflow(input.Mode, input.WorkflowDefinitionId.Value); }
            catch (ArgumentException error) {requirements.Add(new("WORKFLOW_INCOMPATIBLE", "configuration", error.Message, Actions: ["configure"]));}
        }
        // Download-only intake stays independent of file processing. Custom workflows declare
        // their need for instructions through a capability, not a source/site or kind list.
        var consumesInstructions = input.Mode == "library" && input.WorkflowDefinitionId == null;
        if (input.Mode != "lead" && input.WorkflowDefinitionId is { } selectedWorkflow && activities != null)
        {
            var definition = await workflows.GetAsync(selectedWorkflow);
            if (definition != null)
                foreach (var node in definition.Activities)
                    if (activities.Get(node.Kind) is IFileProcessingPlanConsumer consumer)
                    {
                        try { consumesInstructions |= consumer.GetArchivePolicy(node.ConfigJson ?? "{}") == "instructions"; }
                        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
                        { requirements.Add(new("WORKFLOW_INCOMPATIBLE", "configuration", "The workflow has invalid file-processing settings.", Actions: ["configure"])); }
                    }
        }
        var views = new List<DownloadCandidateView>();
        var probeDeadline = DateTimeOffset.UtcNow.AddSeconds(35);
        foreach (var candidate in input.Candidates)
        {
            try
            {
                // Saving a portable lead does not require a working network or an installed adapter.
                var source = input.Mode == "lead"
                    ? DownloadMethodRegistry.Normalize(candidate with {ProviderId = candidate.Kind == "remote" ? candidate.ProviderId : null, MethodId = null})
                    : await registry.ResolveAsync(candidate, ct);
                if (input.Mode != "files" && source.Kind == "remote")
                {
                    var remoteIssue = new DownloadRequirement("REMOTE_PATH_FILES_ONLY", "source",
                        "An account's internal Baidu path can be acquired as files, but cannot be saved as a portable acquisition lead. Choose a share link for library acquisition.");
                    views.Add(new(candidate, [], false, "unsupported", [remoteIssue]));
                    continue;
                }
                var available = await registry.DescribeAllAsync(source, input.AccountId, ct);
                var selectedDescriptor = DownloadMethodCatalog.Select(source);
                var selectedMethodId = selectedDescriptor?.Id ?? "manual";
                var method = available.Single(m => m.Id == selectedMethodId);
                if (method.Ready && input.Mode != "lead") source = await registry.BindAccountAsync(source, input.AccountId, ct);
                var planCheck = ProcessingPlanPreflight.Inspect(source.ExtractionPlanJson, consumesInstructions);
                SourceAvailabilityResult? observation = null;
                var disabled = false;
                if (availability != null && input.IgnoreSourceAvailability != true)
                {
                    // Availability probes may need an account; unavailable integrations do not ask users
                    // to reconnect just to create a plan. Explicit source checks remain independently usable.
                    if (selectedDescriptor?.Availability == null) observation = await availability.GetAsync(source, ct);
                    disabled = await availability.IsDisabledAsync(input.AvailabilityContextKey, source, ct);
                    if (input.CheckSourceAvailability == true && selectedDescriptor?.Availability == null &&
                        DateTimeOffset.UtcNow < probeDeadline && !disabled)
                    {
                        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        budget.CancelAfter(probeDeadline - DateTimeOffset.UtcNow > TimeSpan.Zero
                            ? probeDeadline - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(1));
                        try {observation = await availability.CheckAsync(source, false, budget.Token);}
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* An exhausted check budget stays unknown. */ }
                    }
                }
                var sourceIssues = new List<DownloadRequirement>();
                if (disabled) sourceIssues.Add(new("SOURCE_DISABLED", "source", "This source was disabled for future selections in this task. Restore it or choose another mirror."));
                else if (SourceAvailabilityService.IsConfirmedUnavailable(observation))
                    sourceIssues.Add(new("SOURCE_CONFIRMED_UNAVAILABLE", "source", "The provider confirmed that this share is unavailable. Choose another mirror or check again."));
                else if (observation is {Status: SourceAvailabilityStatuses.AccessRequired} && observation.ExpiresAt > DateTimeOffset.UtcNow &&
                         observation.ReasonCode is "SOURCE_ACCESS_CODE_REQUIRED" or "SOURCE_ACCESS_CODE_REJECTED" or "SOURCE_VERIFICATION_REQUIRED")
                    sourceIssues.Add(new("SOURCE_ACCESS_VERIFICATION_REQUIRED", "source", "Confirm the share access code or required verification, then check this source again.", Actions: ["configure", "checkAgain"]));
                var canSelectManually = (input.Mode == "lead" || method.Ready) && !planCheck.BlocksSubmission &&
                    sourceIssues.All(issue => issue.Code == "SOURCE_CONFIRMED_UNAVAILABLE");
                if (canSelectManually && input.AllowedUnavailableSourceFingerprints.TryGetValue(candidate.Id, out var permittedFingerprint) &&
                    permittedFingerprint == CloudSourceIdentity.Fingerprint(source))
                    sourceIssues.RemoveAll(issue => issue.Code == "SOURCE_CONFIRMED_UNAVAILABLE");
                views.Add(new(candidate, available, (input.Mode == "lead" || method.Ready) && !planCheck.BlocksSubmission && sourceIssues.Count == 0,
                    sourceIssues.Count > 0 ? "unavailable" : planCheck.BlocksSubmission ? "needsSetup" : method.State,
                    method.Requirements.Concat(planCheck.Issues).Concat(sourceIssues).ToList(), source, selectedMethodId, planCheck,
                    observation, disabled, canSelectManually));
            }
            catch (CloudSourceNetworkException error)
            {
                views.Add(new(candidate, [], false, "needsSetup",
                    [new(CloudSourceNetworkException.RequirementCode, "configuration", error.Message, Actions: ["configure", "checkAgain"])]));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                var issue = new DownloadRequirement("SOURCE_INVALID", "source", error.Message);
                views.Add(new(candidate, [], false, "invalid", [issue]));
            }
        }
        var selected = input.SelectedCandidateIds.ToHashSet(StringComparer.Ordinal);
        var chosen = views.Where(v => selected.Contains(v.Candidate.Id)).ToList();
        if (selected.Count != input.SelectedCandidateIds.Count || chosen.Count != selected.Count)
            requirements.Add(new("SELECTION_INVALID", "source", "Select candidates from this input exactly once."));
        if (input.Mode == "library" && selected.Count > 1)
            requirements.Add(new("ONE_LIBRARY_TARGET", "source", "Choose one content target and one mirror for library acquisition."));
        if (input.Mode != "lead" && chosen.Where(v => !string.IsNullOrWhiteSpace(v.Candidate.GroupId))
                .GroupBy(v => v.Candidate.GroupId).Any(g => g.Count() > 1))
            requirements.Add(new("ONE_MIRROR_PER_GROUP", "source", "Choose one mirror for each content group."));
        return new(input.Mode, views, requirements, selected.Count > 0 && requirements.Count == 0 && chosen.All(v => v.CanSelect));
    }

    public async Task<DownloadIntentSubmission> SubmitAsync(DownloadIntentRequest input, string? outputDirectory,
        IReadOnlyList<WorkflowSourceRef>? sourceRefs, string? sourceReference, CancellationToken ct, bool downloadsOnly = false)
    {
        // This check is deliberately repeated immediately before creating resources or workflows.
        var checkedInput = await PreflightAsync(input, outputDirectory, ct);
        if (!checkedInput.CanSubmit)
            throw new InvalidOperationException(string.Join(" ", checkedInput.Requirements.Concat(
                checkedInput.Candidates.Where(v => input.SelectedCandidateIds.Contains(v.Candidate.Id)).SelectMany(v => v.Requirements))
                .Select(r => r.Message).DefaultIfEmpty("Select at least one ready source.")));
        var chosen = checkedInput.Candidates.Where(c => input.SelectedCandidateIds.Contains(c.Candidate.Id)).ToList();
        var selected = chosen.Select(c => c.Candidate).ToList();
        var sources = chosen.Select(c => c.ResolvedSource ?? throw new InvalidOperationException("The selected source was not resolved.")).ToList();
        sourceRefs ??= [];
        if (input.Mode == "files")
        {
            var workset = CloudWorkflow.FromSources(sources, input.Title, Guid.NewGuid().ToString("N"), outputDirectory!)
                with {SourceRefs = sourceRefs.ToList()};
            var post = sourceRefs.FirstOrDefault(r => r.Module == "postParser" && r.EntityKind == "task");
            if (post != null && int.TryParse(post.EntityId, out var taskId) && int.TryParse(post.Revision, out var revision))
                workset = workset with {PostTaskId = taskId, PostRevision = revision};
            var fileRun = await starter.StartFilesAsync(workset, input.AccountId, input.WorkflowDefinitionId, ct, downloadsOnly);
            return new(input.Mode, [fileRun], [], []);
        }
        var recipe = input.Mode == "library" ? await Recipe(input.WorkflowDefinitionId, ct) : null;
        var resourceId = await SaveLeads(input.Title!.Trim(), selected, sources, sourceRefs, sourceReference, ct);
        if (input.Mode == "lead") return new(input.Mode, [], [resourceId], []);
        var source = sources.Single();
        AcquisitionLead? lead = null;
        foreach (var possibleKind in AcquisitionLeadExtensions.StorableKinds)
            if ((lead = await leads.FindByValue(possibleKind, source.Url)) != null) break;
        var candidate = selected.Single() with {Url = source.Url, ProviderId = source.ProviderId,
            AccessCode = source.AccessCode ?? lead?.AccessCode,
            ArchivePassword = source.ArchivePassword ?? lead?.Password,
            ExtractionPlanJson = source.ExtractionPlanJson ?? lead?.ExtractionPlanJson};
        var prepared = new AcquisitionPreparedContext
        {
            SourceRefs = sourceRefs,
            Variables = new Dictionary<string, string>
            {
                ["download.candidate"] = JsonSerializer.Serialize(candidate, Json),
                ["download.resolvedSource"] = JsonSerializer.Serialize(source with
                {
                    AccessCode = candidate.AccessCode, ArchivePassword = candidate.ArchivePassword,
                    ExtractionPlanJson = candidate.ExtractionPlanJson
                }, Json),
                ["download.outputDirectory"] = Path.GetFullPath(outputDirectory!),
                ["download.accountId"] = input.AccountId ?? ""
            }
        };
        var kind = lead?.Kind ?? LeadKind(source);
        var task = await acquisitions.CreatePreparedAsync(resourceId, kind, source.Url, prepared,
            lead?.Id, recipe!.Id, ct: ct);
        var runs = new List<WorkflowRunViewModel>();
        if (task.WorkflowRunId is { } runId && await db.Set<WorkflowRunDbModel>().AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == runId, ct) is { } acquisitionRun)
            runs.Add(WorkflowRunViewModel.From(acquisitionRun.ToDomainModel()));
        return new(input.Mode, runs, [resourceId], [task.Id]);
    }

    private async Task<int> SaveLeads(string title, List<DownloadCandidate> candidates, List<CloudSource> sources,
        IReadOnlyList<WorkflowSourceRef> sourceRefs, string? sourceReference, CancellationToken ct)
    {
        var existing = new List<int>();
        var oldLeads = new Dictionary<string, AcquisitionLead?>();
        if (sources.GroupBy(s => s.Url.NormalizeLeadValue()).Any(group => group.Select(s =>
                (s.AccessCode, s.ArchivePassword, s.ExtractionPlanJson)).Distinct().Count() > 1))
            throw new ArgumentException("The same source has different credentials or file-processing instructions. Select one version.");
        foreach (var source in sources)
        {
            AcquisitionLead? found = null;
            foreach (var kind in AcquisitionLeadExtensions.StorableKinds)
                if ((found = await leads.FindByValue(kind, source.Url)) != null) break;
            oldLeads[source.Id] = found;
            if (found == null) continue;
            existing.Add(found.ResourceId);
            // Reject all conflicts before creating the placeholder or enriching any saved lead.
            if (found.AccessCode != null && source.AccessCode != null && found.AccessCode != source.AccessCode ||
                found.Password != null && source.ArchivePassword != null && found.Password != source.ArchivePassword ||
                !AcquisitionLeadExtensions.CanAcceptExtractionPlan(found.ExtractionPlanJson, source.ExtractionPlanJson))
                throw new InvalidOperationException("A source already has different saved credentials or file-processing instructions. Resolve the conflict first.");
        }
        if (existing.Distinct().Count() > 1) throw new InvalidOperationException("These sources already belong to different resources.");
        var id = existing.FirstOrDefault();
        if (id == 0) id = (await placeholders.CreateByTitle(title, ct)).ResourceId;
        if ((await resources.Get(id))?.HasLocalPath == true) throw new InvalidOperationException("This resource already has local files.");
        foreach (var source in sources)
        {
            var kind = LeadKind(source);
            var old = oldLeads[source.Id];
            var added = await leads.Add(id, new AcquisitionLeadAddInputModel {Kind = old?.Kind ?? kind, Value = source.Url,
                Origin = sourceRefs.Any(r => r.Module == "postParser") ? AcquisitionLeadOrigin.PostParser : AcquisitionLeadOrigin.User,
                AccessCode = source.AccessCode, Password = source.ArchivePassword,
                ExtractionPlanJson = source.ExtractionPlanJson, SourceReference = sourceReference, IsResolved = true});
            if (added.ConflictingResourceId != null || added.Lead == null)
                throw new InvalidOperationException("This source was attached to another resource. Refresh and try again.");
            if (sourceRefs.Count > 0)
            {
                var row = await db.Set<AcquisitionLeadDbModel>().SingleAsync(l => l.Id == added.Lead.Id, ct);
                List<WorkflowSourceRef> prior;
                try {prior = row.SourceRefsJson == null ? [] : JsonSerializer.Deserialize<List<WorkflowSourceRef>>(row.SourceRefsJson, Json) ?? [];}
                catch (JsonException) {prior = [];}
                row.SourceRefsJson = JsonSerializer.Serialize(prior.Concat(sourceRefs).Distinct().ToList(), Json);
                await db.SaveChangesAsync(ct);
            }
        }
        return id;
    }

    public static AcquisitionLeadKind LeadKind(CloudSource source) => source.ProviderId switch
    {"http" => AcquisitionLeadKind.DirectUrl, "magnet" => AcquisitionLeadKind.Magnet,
        "torrent" => AcquisitionLeadKind.Torrent, _ => AcquisitionLeadKind.SharedPage};

    private static void ValidateInput(DownloadIntentRequest input)
    {
        if (input.Mode is not ("files" or "library" or "lead") || input.Candidates == null ||
            input.Candidates.Count is 0 or > 500 || input.Candidates.Any(c => c == null) ||
            input.Candidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != input.Candidates.Count ||
            input.SelectedCandidateIds == null || input.SelectedCandidateIds.Count > 500 || input.Title?.Length > 1024)
            throw new ArgumentException("Provide a valid mode and between one and 500 uniquely identified candidates.");
    }

    private async Task<WorkflowDefinitionDbModel> Recipe(int? explicitId, CancellationToken ct)
    {
        var kinds = new[] {"acquisition.fetchSupported", "fs.processArtifacts", "fs.publishArtifacts", AcquisitionStepKinds.Materialize};
        await RecipeGate.WaitAsync(ct);
        try
        {
            var definition = explicitId == null ? await db.Set<WorkflowDefinitionDbModel>().SingleOrDefaultAsync(d =>
                d.IsBuiltin && d.Name == AcquisitionRecipeName && d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested, ct) : null;
            if (explicitId == null && definition == null)
            {
                var created = await workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
                {
                    Name = AcquisitionRecipeName, Description = "Download supported sources, process their exact manifests, publish verified files and materialize the resource.",
                    TriggerKind = AcquisitionWorkflowKinds.TriggerRequested, Enabled = true,
                    Activities = kinds.Select(k => new WorkflowActivityInputModel {Kind = k,
                        ConfigJson = k == AcquisitionStepKinds.Materialize ? "{\"cleanWorkingDirectory\":false}" : "{}"}).ToList()
                }, ct);
                definition = await db.Set<WorkflowDefinitionDbModel>().SingleAsync(d => d.Id == created.Id, ct);
                definition.IsBuiltin = true;
                await db.SaveChangesAsync(ct);
            }
            var id = explicitId ?? definition!.Id;
            await ValidateWorkflow("library", id);
            return await db.Set<WorkflowDefinitionDbModel>().SingleAsync(d => d.Id == id, ct);
        }
        finally {RecipeGate.Release();}
    }

    private async Task ValidateWorkflow(string mode, int id)
    {
        var workflow = await workflows.GetAsync(id) ?? throw new ArgumentException("Choose an existing workflow.");
        if (workflow.TriggerKind != (mode == "library" ? AcquisitionWorkflowKinds.TriggerRequested : CloudWorkflow.Trigger))
            throw new ArgumentException("This workflow does not accept the selected acquisition mode.");
        if (mode != "library") return;
        var ordered = workflow.Activities.OrderBy(a => a.Order).ToList();
        var required = new[] {"acquisition.fetchSupported", "fs.processArtifacts", "fs.publishArtifacts", AcquisitionStepKinds.Materialize};
        var positions = required.Select(kind => ordered.FindIndex(a => a.Kind == kind)).ToArray();
        if (positions.Any(position => position < 0) || !positions.SequenceEqual(positions.OrderBy(position => position)) ||
            ordered.Any(a => a.Kind is "acquisition.waitForInbox" or "acquisition.unpack"))
            throw new ArgumentException("Choose a workflow using shared download, file processing, publication and materialization in that order.");
        try
        {
            using var config = JsonDocument.Parse(ordered[positions[^1]].ConfigJson ?? "{}");
            if (!config.RootElement.TryGetProperty("cleanWorkingDirectory", out var cleanup) || cleanup.ValueKind != JsonValueKind.False)
                throw new ArgumentException("The materialization node must retain shared working files (cleanWorkingDirectory=false).");
        }
        catch (JsonException error) {throw new ArgumentException("The materialization configuration is invalid.", error);}
    }
}
