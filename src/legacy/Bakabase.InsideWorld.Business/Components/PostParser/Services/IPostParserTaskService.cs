using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bootstrap.Components.Tasks;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public interface IPostParserTaskService
{
    Task<List<PostParserTask>> GetAll();
    Task<PostParserTaskPage> Search(PostParserTaskSearchInput input, CancellationToken ct = default);
    Task<PostParserTaskFilteredSelection> SelectFilteredTasks(PostParserTaskFilterScope scope, int maxTasks,
        CancellationToken ct = default);
    Task<ReParseFailedPostParserTasksInput> GetFailedSelection(CancellationToken ct = default);
    Task<TaskSummary> GetSummary(PostParserSource? source = null);
    Task AddRange(Dictionary<PostParserSource, List<string>> sourceLinksMap, List<PostParseTarget> targets);
    Task AddInputs(Dictionary<PostParserSource, List<string>> sourceLinksMap, List<PostParseTarget> targets,
        List<string> links, string? text, string? title);
    Task Delete(int id);
    Task<int> DeleteByLinks(PostParserSource source, List<string> links);
    Task DeleteAll();
    Task<PostParserTaskDeletionPreview> PreviewDeletion(PostParserTaskFilterScope scope, CancellationToken ct = default);
    Task<PostParserTaskDeletionResult> ConfirmDeletion(ConfirmPostParserTaskDeletionInput input, CancellationToken ct = default);
    Task<PostParserTaskPurchasePreview> PreviewPurchases(PostParserTaskFilterScope scope, CancellationToken ct = default);
    Task<PostParserTaskPurchaseResult> ConfirmPurchases(ConfirmPostParserTaskPurchasesInput input, CancellationToken ct = default);
    Task<PostParserContentReviewPreview> PreviewContentReview(PostParserContentReviewScope scope, CancellationToken ct = default);
    Task<PostParserContentReviewResult> ConfirmContentReview(ConfirmPostParserContentReviewInput input, CancellationToken ct = default);
    Task ReParse(int id);
    Task<PostParserReparseBatchResult> ReParseFailed(ReParseFailedPostParserTasksInput input);
    Task Retry(int id);
    Task Put(int id, PostParserTask pdt);
    Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt, CancellationToken ct);
    Task<Dictionary<string, PostParserTaskStatus>> GetStatusesByLinks(PostParserSource source, List<string> links);
}
