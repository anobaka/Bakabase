using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.InsideWorld.Business;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>Maps a reviewed, saved parsing result into the producer-independent acquisition contract.</summary>
public sealed class PostAcquisitionPlanAdapter(PostDownloadIntentAdapter reader, PostParserTaskExecutionGate gate,
    DownloadIntentService intents, AcquisitionPlanService plans, AcquisitionOutputDirectoryResolver? outputDirectories = null, BakabaseDbContext? correctionDb = null)
{
    public async Task<DownloadIntentSubmission> CreateAsync(PostDownloadIntentRequest input,
        string? directory, CancellationToken ct)
    {
        directory = outputDirectories?.ResolveNew(directory) ?? directory;
        if (input.Mode != "files" || input.WorkflowDefinitionId != null)
            throw new ArgumentException("Create an acquisition plan from the saved result; execution nodes are generated from its instructions.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            // Creating an acquisition neither refreshes nor resumes the producer's parsing run.
            var saved = await reader.Read(input, false, ct);
            var taskOrigin = new WorkflowSourceRef("postParser", "task", input.TaskId.ToString(CultureInfo.InvariantCulture),
                input.Revision.ToString(CultureInfo.InvariantCulture));
            if ((await plans.BySourcesAsync([taskOrigin], ct)).Single().Tasks.Any(t => t.IsDeleted))
                throw new InvalidOperationException("ACQUISITION_TASK_DELETED: This parsing result already belongs to a deleted acquisition task.");
            var check = await intents.PreflightAsync(saved.Input with {CheckSourceAvailability = true}, directory, ct);
            if (!check.CanSubmit)
                throw new InvalidOperationException(string.Join(" ", check.Requirements.Concat(check.Candidates
                    .Where(c => saved.Input.SelectedCandidateIds.Contains(c.Candidate.Id)).SelectMany(c => c.Requirements))
                    .Select(r => r.Message).DefaultIfEmpty("Select ready sources before creating an acquisition.")));

            var chosen = check.Candidates.Where(c => saved.Input.SelectedCandidateIds.Contains(c.Candidate.Id)).ToList();
            var plan = BuildPlan(input, saved, chosen, directory, check.Candidates);
            var origin = new WorkflowSourceRef("postParser", "task", input.TaskId.ToString(CultureInfo.InvariantCulture),
                input.Revision.ToString(CultureInfo.InvariantCulture));
            var refs = new[] {origin}.Concat(plan.Parts.SelectMany(p => p.Sources).Select(s => origin with {ResultKey = s.Id})).Distinct().ToList();
            if (input.CandidateSourceCorrections.Count > 0)
            {
                var existing = (await plans.BySourcesAsync([origin], ct)).Single().Tasks;
                if (existing.Count > 0)
                {
                    // A repeat submission may return its existing frozen plan, but cannot
                    // overwrite a different plan or silently create a second acquisition.
                    if (existing.Count == 1)
                    {
                        var detail = await plans.GetAsync(existing[0].Id, ct);
                        if (detail?.Plan is { } prior && SameReviewedPlan(prior, plan))
                            return new("files", [], [], [detail.Id]);
                    }
                    throw new InvalidOperationException("SOURCE_CORRECTION_EXISTING_ACQUISITION: " +
                        string.Join(",", existing.Select(t => t.Id)));
                }
                // Prepared creation uses multiple SaveChanges calls. Keep its identity,
                // source references and frozen evidence atomic for uncertain HTTP results.
                if (correctionDb == null) throw new InvalidOperationException("Source correction storage is unavailable.");
                await using var transaction = await correctionDb.Database.BeginTransactionAsync(ct);
                var corrected = await plans.CreatePreparedPlanAsync(plan, refs, ct);
                await transaction.CommitAsync(ct);
                return new("files", [], [], [corrected.Id]);
            }
            var task = await plans.CreatePreparedPlanAsync(plan, refs, ct);
            return new("files", [], [], [task.Id]);
        }
        finally {gate.Semaphore.Release();}
    }
    internal static AcquisitionPlan BuildPlan(PostDownloadIntentRequest input,
        (DownloadIntentRequest Input, string? SourceReference,
            IReadOnlyDictionary<string, Bakabase.Service.Components.Acquisition.ProcessingPlans.SourceProcessingPlanView> ProcessingPlans,
            Bakabase.Modules.PostParser.Models.Domain.PostDownloadMediaView Media) saved,
        IReadOnlyList<DownloadCandidateView> chosen, string? directory,
        IReadOnlyList<DownloadCandidateView>? checkedCandidates = null, bool automaticSelection = false,
        IReadOnlyList<string>? providerPriority = null)
    {
        var origin = new WorkflowSourceRef("postParser", "task", input.TaskId.ToString(CultureInfo.InvariantCulture),
            input.Revision.ToString(CultureInfo.InvariantCulture));
        var checkedById = (checkedCandidates ?? chosen).ToDictionary(c => c.Candidate.Id, StringComparer.Ordinal);
        AcquisitionPlanSource Freeze(DownloadCandidate candidate)
        {
            var resolved = checkedById.GetValueOrDefault(candidate.Id)?.ResolvedSource;
            // Preserve unreadable/unsupported alternatives as evidence; only the selected source
            // needs to resolve at preparation. Its sibling's credentials and instructions stay local.
            FileProcessingPlan? processing = null;
            string? processingIssue = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(candidate.ExtractionPlanJson))
                    processing = FileProcessingPlanValidator.Validate(JsonSerializer.Deserialize<FileProcessingPlan>(candidate.ExtractionPlanJson, JsonSerializerOptions.Web));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            {
                if (chosen.Any(c => c.Candidate.Id == candidate.Id)) throw;
                processingIssue = "SOURCE_PROCESSING_INVALID";
            }
            return new()
            {
                Id = candidate.Id, Url = resolved?.Url ?? candidate.Url,
                OriginalUrl = resolved?.OriginalUrl, ResolutionChain = resolved?.ResolutionChain.ToList() ?? [],
                Kind = resolved?.Kind ?? candidate.Kind, ProviderId = resolved?.ProviderId ?? candidate.ProviderId,
                AccessCode = candidate.AccessCode, ArchivePassword = candidate.ArchivePassword,
                MethodId = candidate.MethodId ?? resolved?.MethodId, AccountId = candidate.AccountId ?? resolved?.AccountId,
                SourceCorrection = candidate.SourceCorrection?.Clone(), Processing = processing,
                OriginalProcessingPlanJson = processingIssue != null ? candidate.ExtractionPlanJson : null, ProcessingIssueCode = processingIssue,
                ProcessingSource = origin with {ResultKey = candidate.Id},
                AppliedProcessingRevisionId = input.CandidateProcessingPlans.ContainsKey(candidate.Id) ? null :
                    saved.ProcessingPlans.GetValueOrDefault(candidate.Id) is {EffectiveRevision: > 0} effective ? effective.History.First().Id : null
            };
        }
        return new AcquisitionPlan
        {
            Title = saved.Input.Title?.Trim() ?? "", OutputDirectory = directory ?? "",
            SourceProviderPriority = providerPriority?.ToList() ?? [],
            CoverCandidates = saved.Media.CoverCandidates.Select(c => new AcquisitionCoverCandidate
                {Url = c.Url, Kind = c.Kind, Evidence = c.Evidence}).ToList(),
            Parts = chosen.Select(c =>
            {
                _ = c.ResolvedSource ?? throw new InvalidOperationException("The selected source could not be resolved.");
                var selected = Freeze(c.Candidate);
                var mirrors = c.Candidate.MirrorGroupConfirmed == true && !string.IsNullOrWhiteSpace(c.Candidate.GroupId)
                    ? saved.Input.Candidates.Where(m => m.MirrorGroupConfirmed == true && m.GroupId == c.Candidate.GroupId).ToList()
                    : [c.Candidate];
                return new AcquisitionPlanPart
                {
                    Id = "part-" + CloudOperationService.Key(string.IsNullOrWhiteSpace(c.Candidate.GroupId)
                        ? "source:" + c.Candidate.Id : "group:" + c.Candidate.GroupId)[..20],
                    Title = c.Candidate.Title, SelectedSourceId = selected.Id,
                    SelectionMode = automaticSelection ? "automatic" : "manual",
                    ProcessingSource = selected.ProcessingSource, AppliedProcessingRevisionId = selected.AppliedProcessingRevisionId,
                    Sources = mirrors.Select(Freeze).ToList(), Processing = selected.Processing
                };
            }).ToList()
        };
    }

    private static bool SameReviewedPlan(AcquisitionPlan previous, AcquisitionPlan current)
    {
        var priorCorrections = previous.Parts.SelectMany(p => p.Sources).Where(s => s.SourceCorrection != null)
            .Select(s => CorrectionFingerprint(s.SourceCorrection)).Order().ToList();
        var currentCorrections = current.Parts.SelectMany(p => p.Sources).Where(s => s.SourceCorrection != null)
            .Select(s => CorrectionFingerprint(s.SourceCorrection)).Order().ToList();
        // Prepared creation assigns the task's directories and storage defaults after it receives an ID.
        // Compare the same frozen settings on a retry while retaining the requested output root and all source instructions.
        var expected = current with
        {
            ExtractionLayout = current.ExtractionLayout ?? previous.ExtractionLayout,
            DeliveryLayout = previous.DeliveryLayout,
            StageDirectories = previous.StageDirectories,
            StoragePolicy = previous.StoragePolicy
        };
        return priorCorrections.Count > 0 && priorCorrections.All(f => f != null) && priorCorrections.SequenceEqual(currentCorrections) &&
            JsonElement.DeepEquals(JsonSerializer.SerializeToElement(previous, JsonSerializerOptions.Web),
                JsonSerializer.SerializeToElement(expected, JsonSerializerOptions.Web));
    }

    private static string? CorrectionFingerprint(JsonElement? correction) =>
        correction is {ValueKind: JsonValueKind.Object} value && value.TryGetProperty("fingerprint", out var fingerprint) &&
        fingerprint.ValueKind == JsonValueKind.String ? fingerprint.GetString() : null;

}
