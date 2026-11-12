using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

public static class AcquisitionPlanCompiler
{
    public static void Validate(AcquisitionPlan plan)
    {
        if (plan.DownloadLayout is not (null or "currentDirectory" or "separateDirectory"))
            throw new ArgumentException("Invalid local download layout.");
        if (!FileExtractionLayouts.Valid(plan.ExtractionLayout)) throw new ArgumentException("Invalid archive extraction layout.");
        if (plan.Version != 1 || plan.Delivery != "files" || string.IsNullOrWhiteSpace(plan.Title) || plan.Title.Length > 512 ||
            plan.Parts.Count is 0 or > 100 || plan.Parts.Select(p => p.Id).Distinct().Count() != plan.Parts.Count)
            throw new ArgumentException("Provide a supported acquisition plan with a title and explicitly selected parts.");
        if (plan.AdaptiveFileProcessingMaxIterations is < 1 or > 5)
            throw new ArgumentException("Adaptive file processing must use between one and five assessment rounds.");
        if (plan.CoverCandidates == null || plan.CoverCandidates.Count > 128 || plan.CoverCandidates.Any(c => c == null ||
            string.IsNullOrWhiteSpace(c.Url) || c.Url.Length > 2048 || !Uri.TryCreate(c.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || c.Kind is not ("cover" or "preview") ||
            c.Evidence == null || c.Evidence.Count > 8 || c.Evidence.Any(e => e == null || e.Length > 300)))
            throw new ArgumentException("Provide bounded cover candidates with HTTP image addresses and supported roles.");
        if (plan.StoragePolicy != null) ArtifactStoragePolicies.Validate(plan.StoragePolicy);
        if (plan.DeliveryLayout != null) AcquisitionDeliveryDirectoryResolver.ValidateLayout(plan.DeliveryLayout, plan.Parts.Select(p => p.Id).ToList());
        if (plan.StageDirectories != null) AcquisitionStageDirectoryResolver.Validate(plan.StageDirectories, plan);
        if (plan.PublishRetainedFiles)
        {
            var hasLayoutPreview = plan.Parts.Any(p => p.DeliveryProjectionFingerprint != null);
            if (plan.Parts.Any(p => p.ContinueFrom == null || p.Processing is not {Requirement: "notRequired", Steps.Count: 0}) ||
                hasLayoutPreview && (plan.DeliveryLayout?.Settings.ContentLayout == null ||
                    plan.Parts.Any(p => p.DeliveryProjectionFingerprint is not {Length: 64} fp || !fp.All(Uri.IsHexDigit))))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
        }
        var priority = plan.SourceProviderPriority ?? [];
        if (priority.Count > 100 || priority.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 128) ||
            priority.Distinct(StringComparer.Ordinal).Count() != priority.Count)
            throw new ArgumentException("Provide a bounded source priority without duplicates.");
        var sources = plan.Parts.SelectMany(p => p.Sources).ToList();
        if (sources.Count > 500 || sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new ArgumentException("Every source needs a globally unique identity; provide at most 500 sources.");
        foreach (var part in plan.Parts)
        {
            if (!SafeId(part.Id) || part.Sources.Count is 0 or > 100 || part.Sources.Select(s => s.Id).Distinct().Count() != part.Sources.Count ||
                !part.Sources.Any(s => s.Id == part.SelectedSourceId))
                throw new ArgumentException("Every required part needs a unique identity and one explicitly selected source.");
            if (part.Sources.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.Id.Length > 128 || string.IsNullOrWhiteSpace(s.Url) || s.Url.Length > 8192 ||
                    s.OriginalUrl?.Length > 8192 || s.OriginalProcessingPlanJson?.Length > 131072 || s.ProcessingIssueCode?.Length > 128 || s.ResolutionChain?.Count > 8 ||
                    s.ResolutionChain?.Any(url => string.IsNullOrWhiteSpace(url) || url.Length > 8192) == true))
                throw new ArgumentException("The plan contains an invalid source.");
            if (part.SelectionMode is not (null or "manual" or "automatic"))
                throw new ArgumentException("Choose manual or automatic mirror selection.");
            if (part.ContinueFrom is { } input && (input.TaskId <= 0 || input.PreviousRunId <= 0 ||
                new[] {input.ArtifactId, input.OperationId, input.Fingerprint}.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 128)))
                throw new ArgumentException("PLAN_REPLAN_EVIDENCE_INVALID");
            if (part.UseAdaptiveAssessment == true && (part.ContinueFrom == null ||
                Processing(part) is not {Requirement: "unknown", Steps.Count: 0}))
                throw new ArgumentException("PLAN_REPLAN_INVALID");
            if (part.ContinueFrom == null && part.Sources.Single(s => s.Id == part.SelectedSourceId).ProcessingIssueCode != null)
                throw new ArgumentException("SOURCE_PROCESSING_INVALID: Review the selected source's retained instructions.");
            var storagePolicy = ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), part.StoragePolicy);
            foreach (var processing in part.Sources.Select(s => s.Processing).Append(part.Processing).Where(p => p != null))
                foreach (var step in processing!.Steps) _ = ArtifactStoragePolicies.Resolve(storagePolicy, step.StoragePolicy);
            FileProcessingPlanValidator.Validate(part.Processing);
            foreach (var source in part.Sources) FileProcessingPlanValidator.Validate(source.Processing);
        }
    }
    private static bool SafeId(string id) => Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,80}$");

    public static WorkflowRunPlan Compile(AcquisitionPlan plan, IReadOnlyList<CloudSource>? resolvedSources = null,
        WorkflowInvocationInput? invocation = null)
    {
        Validate(plan);
        var downloading = plan.Parts.Where(p => p.ContinueFrom == null).ToList();
        Dictionary<string, CloudSource>? resolved = null;
        if (resolvedSources != null)
        {
            var selectedIds = downloading.Select(p => p.SelectedSourceId!).ToHashSet(StringComparer.Ordinal);
            if (resolvedSources.Count != selectedIds.Count ||
                resolvedSources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != resolvedSources.Count ||
                resolvedSources.Any(s => !selectedIds.Contains(s.Id)))
                throw new ArgumentException("Checked sources must match the plan's selected source identities.");
            resolved = resolvedSources.ToDictionary(s => s.Id, StringComparer.Ordinal);
        }
        var nodes = new List<WorkflowRunPlanNode>();
        // Only plans with a captured caller policy opt in. Legacy saved plans keep their exact
        // node shape and placement; no global settings are read while compiling or resuming.
        var parameterized = plan.StoragePolicy != null;
        var declarations = parameterized ? ArtifactWorkflowParameters.Declarations().ToList() : [];
        var callerValues = parameterized ? ArtifactWorkflowParameters.Values(plan.StoragePolicy!).ToDictionary(e => e.Key, e => e.Value) : [];
        var localOverrides = plan.Parts.Where(p => p.StoragePolicy != null)
            .ToDictionary(p => p.Id, p => p.StoragePolicy!, StringComparer.Ordinal);
        if (downloading.Count > 0)
        {
            if (parameterized)
            {
                declarations.Add(new() {Key = "storage.downloadDirectory", Type = "string", Format = "directory", Required = true, DisplayInRun = true});
                callerValues.Add("storage.downloadDirectory", JsonSerializer.SerializeToElement(plan.OutputDirectory));
            }
            nodes.Add(new() {NodeId = "sources.select", Kind = "download.selectSources",
                ConfigJson = parameterized ? JsonSerializer.Serialize(new CloudWorkflowConfig
                    {StoragePolicy = new(), OutputDirectory = plan.OutputDirectory, TargetStorageOverrides = localOverrides}, WorkflowJson.Options) : "{}",
                ParameterBindings = parameterized ? ArtifactWorkflowParameters.Bindings().Append(new WorkflowParameterBinding
                    {ParameterKey = "storage.downloadDirectory", ConfigPath = "/outputDirectory"}).ToArray() : []});
            // Compile checked providers while preserving the original saved locator.
            if (downloading.Any(p => (resolved?.GetValueOrDefault(p.SelectedSourceId!) ??
                    ToSource(p.Sources.Single(s => s.Id == p.SelectedSourceId), p)).ProviderId == "baidu"))
            {
                if (plan.DownloadLayout != null)
                {
                    declarations.Add(new() {Key = "download.layout", Type = "string", Required = true, DisplayInRun = true,
                        AllowedValues = [JsonSerializer.SerializeToElement("currentDirectory"), JsonSerializer.SerializeToElement("separateDirectory")]});
                    callerValues.Add("download.layout", JsonSerializer.SerializeToElement(plan.DownloadLayout));
                }
                nodes.Add(new() {NodeId = "sources.transfer", Kind = "baidu.transfer",
                    ConfigJson = plan.DownloadLayout == null ? "{}" : JsonSerializer.Serialize(new {downloadLayout = plan.DownloadLayout}, WorkflowJson.Options),
                    ParameterBindings = plan.DownloadLayout == null ? [] : [new() {ParameterKey = "download.layout", ConfigPath = "/downloadLayout"}]});
            }
            nodes.Add(new() {NodeId = "sources.enqueue", Kind = "download.enqueueSupported"});
            nodes.Add(new() {NodeId = "sources.await", Kind = "download.awaitCompletion"});
        }
        var continued = plan.Parts.Where(p => p.ContinueFrom != null).ToList();
        if (continued.Count > 0)
            nodes.Add(new() {NodeId = "files.continue", Kind = AcquisitionPlanContinueFilesActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanContinueFilesConfig(continued.Select(p =>
                    new AcquisitionPlanContinuedTarget(Target(plan, p), p.ContinueFrom!)
                        {ExpectedDeliveryProjectionFingerprint = plan.PublishRetainedFiles ? p.DeliveryProjectionFingerprint : null}).ToArray())
                    {StoragePolicy = parameterized ? new() : null,
                        PublishRetainedFiles = plan.PublishRetainedFiles,
                        ContentLayout = plan.PublishRetainedFiles ? plan.DeliveryLayout?.Settings.ContentLayout : null,
                        TargetStorageOverrides = parameterized ? localOverrides : null}, WorkflowJson.Options),
                ParameterBindings = parameterized ? ArtifactWorkflowParameters.Bindings() : []});
        foreach (var part in plan.Parts.Where(_ => !plan.PublishRetainedFiles))
        {
            var processing = Processing(part);
            if (processing.Steps.Count == 0)
                nodes.Add(Node(plan, part, null, processing));
            else
                foreach (var step in processing.Steps) nodes.Add(Node(plan, part, step.Id, processing));
        }
        // Assessment is an explicit non-transparent node. It never changes saved running plans
        // and never automatically extracts another layer or reacquires its downloaded source.
        // A local review may receive its extraction instructions only when the user answers
        // the confirmation node. Keep the existing assessment after that dynamic answer too.
        foreach (var part in plan.Parts.Where(p => !plan.PublishRetainedFiles &&
                     (Processing(p).Steps.Any(s => s.Op == "extractArchive") ||
                      Processing(p).Requirement == "unknown" || p.ContinueFrom != null && Adaptive(plan, p))))
            nodes.Add(new() {NodeId = part.Id + ".assessDelivery", Kind = AssessFileArtifactDeliveryActivity.ActivityKind,
                ConfigJson = JsonSerializer.Serialize(new FileArtifactDeliveryAssessmentConfig(part.Id,
                    ExtractionLayout: plan.ExtractionLayout,
                    EnableAdaptiveAssessment: Adaptive(plan, part),
                    RequiresAdaptiveDecision: part.UseAdaptiveAssessment == true,
                    MaxAdaptiveIterations: Math.Clamp(plan.AdaptiveFileProcessingMaxIterations, 1, 5),
                    AdaptiveContext: plan.AdaptiveFileProcessingContext), WorkflowJson.Options)});
        var bindsDelivery = parameterized && plan.DeliveryLayout != null;
        if (bindsDelivery)
        {
            declarations.Add(new() {Key = "storage.deliveryDirectories", Type = "object", Required = true});
            callerValues.Add("storage.deliveryDirectories", JsonSerializer.SerializeToElement(plan.DeliveryLayout!.PartDirectories));
        }
        var contentLayout = plan.DeliveryLayout?.Settings.ContentLayout;
        var deliveryBindings = bindsDelivery ? new List<WorkflowParameterBinding>
            {new() {ParameterKey = "storage.deliveryDirectories", ConfigPath = "/targetDirectories"}} : [];
        if (contentLayout != null)
        {
            declarations.Add(new() {Key = "delivery.contentLayout", Type = "object", Required = true});
            callerValues.Add("delivery.contentLayout", JsonSerializer.SerializeToElement(contentLayout, WorkflowJson.Options));
            deliveryBindings.Add(new() {ParameterKey = "delivery.contentLayout", ConfigPath = "/contentLayout"});
        }
        nodes.Add(new() {NodeId = "files.deliver", Kind = "fs.publishArtifacts", ConfigJson = plan.DeliveryLayout == null ? "{}" :
            JsonSerializer.Serialize(new FileArtifactPublicationConfig {TargetDirectories = plan.DeliveryLayout.PartDirectories,
                ContentLayout = contentLayout}, WorkflowJson.Options),
            ParameterBindings = deliveryBindings});
        // A local continuation borrows a previously verified artifact. It neither downloads
        // a source nor grants this run permission to clean the original run's inputs.
        // Only targets acquired by this plan have a registered download receipt to clean.
        foreach (var part in downloading)
        {
            if (parameterized)
                nodes.Add(new() {NodeId = part.Id + ".cleanup", Kind = "download.cleanupArtifacts", ConfigJson =
                    JsonSerializer.Serialize(new ArtifactCleanupConfig {TargetId = part.Id, UseTargetStoragePolicy = true}, WorkflowJson.Options)});
        }
        var compiled = new WorkflowRunPlan {Name = plan.Title, TriggerKind = CloudWorkflow.Trigger, Nodes = nodes,
            Parameters = declarations};
        if (declarations.Count > 0 || invocation != null)
        {
            if (invocation?.CallerDefaults != null)
                foreach (var (key, value) in invocation.CallerDefaults) callerValues[key] = value;
            compiled = WorkflowInvocationResolver.Resolve(compiled, new WorkflowInvocationInput
                {CallerDefaults = callerValues, Overrides = invocation?.Overrides});
        }
        _ = WorkflowRunPlans.Serialize(compiled);
        return compiled;
    }

    private static bool Adaptive(AcquisitionPlan plan, AcquisitionPlanPart part) =>
        part.UseAdaptiveAssessment ?? plan.AdaptiveFileProcessing;

    private static WorkflowRunPlanNode Node(AcquisitionPlan plan, AcquisitionPlanPart part, string? stepId, FileProcessingPlan processing) => new()
    {
        NodeId = part.Id + ".process." + (stepId == null ? "confirm" : CloudOperationService.Key(stepId)[..16]),
        Kind = AcquisitionPlanProcessingActivity.ActivityKind,
        Notes = stepId == null ? "Confirm file processing" : processing.Steps.Single(s => s.Id == stepId).Op +
            " (input: " + string.Join(", ", processing.Steps.Single(s => s.Id == stepId).GetInputReferences()) + ")",
        ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig(part.Id, processing, stepId, part.ContinueFrom, Policy(plan, part), plan.ExtractionLayout,
            UseVerifiedInput: part.ContinueFrom != null && (part.UseAdaptiveAssessment == true ||
                part.UseAdaptiveAssessment == false && processing.Requirement == "notRequired")), WorkflowJson.Options),
        ParameterBindings = plan.StoragePolicy == null ? [] : ArtifactWorkflowParameters.Bindings(part.StoragePolicy)
    };

    public static DownloadWorkset Workset(int taskId, int revision, AcquisitionPlan plan, IReadOnlyList<WorkflowSourceRef> sources) => new()
    {
        Id = "acquisition:" + taskId + ":" + revision, Title = plan.Title, OutputDirectory = plan.OutputDirectory,
        SourceRefs = sources.ToList(), Targets = plan.Parts.Where(p => p.ContinueFrom == null ||
            plan.Parts.All(part => part.ContinueFrom != null)).Select(p => Target(plan, p)).ToList()
    };

    private static CloudDownloadTarget Target(AcquisitionPlan plan, AcquisitionPlanPart p) => new()
    {
        Id = p.Id, Title = p.Title ?? plan.Title, SelectedSourceId = p.SelectedSourceId, StoragePolicy = Policy(plan, p),
        DownloadDirectoryScope = plan.StageDirectories?.Parts.GetValueOrDefault(p.Id)?.Download?.Allocation,
        ProcessingDirectoryScope = plan.StageDirectories?.Parts.GetValueOrDefault(p.Id)?.Processing?.Allocation,
        // Alternatives remain in the versioned plan, outside the frozen execution.
        Sources = p.Sources.Where(s => s.Id == p.SelectedSourceId).Select(s => ToSource(s, p)).ToList(),
        AccountId = p.Sources.Single(s => s.Id == p.SelectedSourceId).AccountId,
        ProcessingPlanJson = JsonSerializer.Serialize(Processing(p), WorkflowJson.Options)
    };

    internal static FileProcessingPlan Processing(AcquisitionPlanPart part) => part.Processing ??
        part.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId)?.Processing ?? new();
    private static ArtifactStoragePolicy? Policy(AcquisitionPlan plan, AcquisitionPlanPart part) => plan.StoragePolicy == null ? null :
        ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy);

    public static CloudSource ToSource(AcquisitionPlanSource s, AcquisitionPlanPart part) => DownloadMethodRegistry.Normalize(new DownloadCandidate
    {
        Id = s.Id, Url = s.Url, Kind = s.Kind, ProviderId = s.ProviderId, AccessCode = s.AccessCode,
        ArchivePassword = s.ArchivePassword, AccountId = s.AccountId, MethodId = s.MethodId,
        ExtractionPlanJson = JsonSerializer.Serialize(s.Id == part.SelectedSourceId
            ? Processing(part) : s.Processing ?? new(), WorkflowJson.Options)
    }) with {OriginalUrl = s.OriginalUrl, ResolutionChain = s.ResolutionChain ?? []};
}
