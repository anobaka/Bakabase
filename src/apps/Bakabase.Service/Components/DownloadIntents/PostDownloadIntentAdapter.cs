using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CsQuery;
using Bakabase.Service.Components.Acquisition.Handoffs;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>The only intent component aware of post tasks. Addresses and credentials come from the saved result.</summary>
public sealed class PostDownloadIntentAdapter(BakabaseDbContext db, PostParserTaskExecutionGate gate,
    PostParserWorkflowService<BakabaseDbContext> posts, DownloadIntentService intents, SourceProcessingPlanService? processingPlans = null)
{
    public async Task<DownloadIntentPreflight> PreflightAsync(PostDownloadIntentRequest input,
        string? directory, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try { return await intents.PreflightAsync((await Read(input, false, ct)).Input, directory, ct); }
        finally {gate.Semaphore.Release();}
    }

    public async Task<DownloadIntentSubmission> SubmitAsync(PostDownloadIntentRequest input,
        string? directory, CancellationToken ct)
    {
        if (input.CandidateSourceCorrections?.Count > 0)
            throw new ArgumentException("SOURCE_CORRECTION_REQUIRES_ACQUISITION_PLAN: Save reviewed source evidence in an acquisition plan.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var saved = await Read(input, true, ct);
            var source = new WorkflowSourceRef("postParser", "task", input.TaskId.ToString(CultureInfo.InvariantCulture), input.Revision.ToString(CultureInfo.InvariantCulture));
            return await intents.SubmitAsync(saved.Input, directory,
                new[] {source}.Concat(input.ResourceIndices.Select(i => source with {ResultKey = i.ToString(CultureInfo.InvariantCulture)})).ToList(),
                saved.SourceReference, ct);
        }
        finally {gate.Semaphore.Release();}
    }

    internal async Task<(DownloadIntentRequest Input, string? SourceReference, IReadOnlyDictionary<string, SourceProcessingPlanView> ProcessingPlans, PostDownloadMediaView Media)> Read(PostDownloadIntentRequest input, bool refresh, CancellationToken ct)
    {
        if (input.ResourceIndices == null || input.CandidateMethods == null || input.CandidateAccounts == null ||
            input.CandidateProcessingPlans == null || input.CandidateSourceCorrections == null || input.CandidateSourceCorrections.Count > 100 ||
            input.CandidateProcessingPlans.Count > 500 ||
            input.CandidateProcessingPlans.Values.Any(p => p == null || p.Length > 131072))
            throw new ArgumentException("Provide valid source selections and bounded processing instructions.");
        if (refresh) await posts.RefreshTaskUnderGateAsync(input.TaskId, ct);
        var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TaskId && !t.IsDeleted, ct)
            ?? throw new ArgumentException("The saved parsing result no longer exists.");
        if (task.Revision != input.Revision) throw new InvalidOperationException("The post was parsed again. Refresh the results before acquiring.");
        if (task.ParsingState != "complete" || string.IsNullOrWhiteSpace(task.Results) ||
            task.Error != null && (task.Error != PostParserSavedResults.MissingWorkflowRunError ||
                !PostParserSavedResults.HasCompletedDownloadResult(task)))
            throw new ArgumentException("Complete parsing before acquiring the saved results.");
        var info = PostParserSavedResults.ReadDownloadInfo(task);
        var resources = info.Resources;
        var mirrorGroups = (info.Groups ?? []).Where(g => g != null).GroupBy(g => g.Id)
            .Where(g => g.Count() == 1 && g.Single().Kind == "main").Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        PostContent? snapshot = null;
        if (!string.IsNullOrWhiteSpace(task.ContentSnapshotJson))
            try {snapshot = JsonSerializer.Deserialize<PostContent>(task.ContentSnapshotJson, JsonSerializerOptions.Web);}
            catch (JsonException) { }
        var corrections = ValidateSourceCorrections(input, task.Results, snapshot, resources);
        var media = PostDownloadMedia.AnalyzeSaved(snapshot, info);
        var excluded = media.ExcludedResourceIndices.ToHashSet();
        if (input.ResourceIndices.Any(excluded.Contains))
            throw new ArgumentException("POST_PREVIEW_NOT_DOWNLOAD: Embedded previews are cover candidates, not download sources.");
        IReadOnlyDictionary<string, SourceProcessingPlanView> effective = new Dictionary<string, SourceProcessingPlanView>();
        if (processingPlans != null)
        {
            await PostProcessingPlanSourceReader.EnsureStableAsync(db, task, ct);
            var views = await processingPlans.ResolveAsync(PostProcessingPlanSourceReader.Snapshots(task), ct);
            effective = views.ToDictionary(v => v.Source.ResultKey!, StringComparer.Ordinal);
        }
        if (input.ResourceIndices.Any(i => i < 0 || i >= resources.Count) ||
            input.CandidateMethods.Keys.Concat(input.CandidateAccounts.Keys).Concat(input.CandidateProcessingPlans.Keys)
                .Any(k => !int.TryParse(k, out var i) || i < 0 || i >= resources.Count))
            throw new ArgumentException("Choose candidates from the current saved result.");
        return (new DownloadIntentRequest
        {
            Mode = input.Mode, Title = input.Title ?? task.Title, AccountId = input.AccountId,
            AvailabilityContextKey = $"postParser:{task.Id}:{task.Revision}",
            WorkflowDefinitionId = input.WorkflowDefinitionId,
            SelectedCandidateIds = input.ResourceIndices.Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList(),
            Candidates = resources.Select((resource, index) => new {resource, index}).Where(item => !excluded.Contains(item.index))
                .Select(item => {var resource = item.resource; var index = item.index; return new DownloadCandidate
            {
                Id = index.ToString(CultureInfo.InvariantCulture),
                Url = input.CandidateSourceCorrections.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture))?.Url ?? resource.Link,
                Title = task.Title, GroupId = resource.GroupId,
                MirrorGroupConfirmed = resource.GroupId != null && mirrorGroups.Contains(resource.GroupId),
                AccessCode = input.CandidateSourceCorrections.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var correction)
                    ? correction.AccessCode : resource.Code,
                ArchivePassword = resource.Password, SourceCorrection = corrections.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)),
                ExtractionPlanJson = input.CandidateProcessingPlans.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)) ??
                    ((effective.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture))?.EffectivePlan ?? resource.Extraction) is { } plan
                        ? JsonSerializer.Serialize(plan, JsonSerializerOptions.Web) : null),
                MethodId = input.CandidateMethods.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)),
                AccountId = input.CandidateAccounts.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture))
            };}).ToList()
        }, string.IsNullOrWhiteSpace(task.Text) ? task.Link : null, effective, media);
    }
    private static Dictionary<string, JsonElement?> ValidateSourceCorrections(PostDownloadIntentRequest input,
        string results, PostContent? snapshot, IReadOnlyList<PostDownloadResource> resources)
    {
        var output = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
        if (input.CandidateSourceCorrections.Count == 0) return output;
        if (!Hash(input.ExpectedResultsHash) || !string.Equals(input.ExpectedResultsHash,
                PostAcquisitionResultFingerprint.Hash(results), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SOURCE_CORRECTION_RESULT_CHANGED: Refresh the saved-result selection before correcting sources.");
        foreach (var (key, correction) in input.CandidateSourceCorrections)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                key != index.ToString(CultureInfo.InvariantCulture) || index < 0 || index >= resources.Count ||
                !input.ResourceIndices.Contains(index) || correction == null || correction.ResourceIndex != index)
                throw new ArgumentException("SOURCE_CORRECTION_SELECTION_INVALID");
            if (correction.ExpectedOriginalUrl == null || correction.ExpectedOriginalUrl != resources[index].Link)
                throw new InvalidOperationException("SOURCE_CORRECTION_ORIGINAL_CHANGED");
            if (correction.Codec != "qr" || !SourceUrl(correction.Url) || correction.Url != correction.DecodedValue ||
                correction.AccessCode?.Length > 1024 || correction.ExpectedOriginalUrl.Length > 8192 ||
                !SourceUrl(correction.ImageUrl) || !Hash(correction.SourceHtmlSha256) || !Hash(correction.ImageSha256) ||
                correction.ImageIndex < 0 || correction.ImageIndex > 10000)
                throw new ArgumentException("SOURCE_CORRECTION_EVIDENCE_INVALID");
            var html = correction.ImageScope switch
            {
                "mainHtml" when correction.CommentIndex == null => snapshot?.MainHtml,
                "commentHtmlList" when snapshot != null && correction.CommentIndex is >= 0 && correction.CommentIndex < snapshot.CommentHtmlList.Count =>
                    snapshot.CommentHtmlList[correction.CommentIndex.Value],
                _ => null
            };
            if (html == null || !string.Equals(TextHash(html), correction.SourceHtmlSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SOURCE_CORRECTION_SNAPSHOT_CHANGED");
            var images = CQ.CreateFragment(html)["img"].ToList();
            if (correction.ImageIndex >= images.Count)
                throw new ArgumentException("SOURCE_CORRECTION_IMAGE_NOT_IN_SNAPSHOT");
            var image = images[correction.ImageIndex];
            if (image.GetAttribute("src") != correction.ImageUrl && image.GetAttribute("data-original-src") != correction.ImageUrl)
                throw new ArgumentException("SOURCE_CORRECTION_IMAGE_NOT_IN_SNAPSHOT");
            // The caller supplies and reviews the decoded value and image hash. We bind that
            // assertion to immutable saved HTML; this endpoint neither fetches images nor runs OCR.
            var evidence = new
            {
                kind = "userReviewedQr", source = new WorkflowSourceRef("postParser", "task",
                    input.TaskId.ToString(CultureInfo.InvariantCulture), input.Revision.ToString(CultureInfo.InvariantCulture), key),
                resultsHash = input.ExpectedResultsHash!.ToLowerInvariant(),
                originalUrl = correction.ExpectedOriginalUrl, correctedUrl = correction.Url, accessCode = correction.AccessCode,
                imageScope = correction.ImageScope, commentIndex = correction.CommentIndex, imageIndex = correction.ImageIndex,
                imageUrl = correction.ImageUrl, sourceHtmlSha256 = correction.SourceHtmlSha256.ToLowerInvariant(),
                imageSha256 = correction.ImageSha256.ToLowerInvariant(), codec = correction.Codec, decodedValue = correction.DecodedValue
            };
            var frozen = JsonSerializer.SerializeToElement(evidence, JsonSerializerOptions.Web);
            output[key] = JsonSerializer.SerializeToElement(new
                {fingerprint = TextHash(frozen.GetRawText()), evidence = frozen}, JsonSerializerOptions.Web);
        }
        return output;
    }

    private static bool Hash(string? value) => value is {Length: 64} && value.All(char.IsAsciiHexDigit);
    private static string TextHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool SourceUrl(string? value) => value is {Length: > 0 and <= 8192} &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

}
