using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms;

/// <summary>
/// Deterministic search transform: runs a <see cref="WorkflowQueryItem"/>'s query against
/// ExHentai and emits the first gallery as an ExHentai-typed <see cref="SubscriptionItem"/>
/// (consumable by the existing download action). Drops the item when there's no hit.
/// </summary>
public class ExHentaiQueryToGalleryActivity : IWorkflowActivity
{
    private readonly ExHentaiClient _client;

    public ExHentaiQueryToGalleryActivity(ExHentaiClient client)
    {
        _client = client;
    }

    public string Kind { get; } = ExHentaiTransformWorkflowActivityKinds.QueryToGallery;
    public string DisplayName => "ExHentai: search query → first gallery";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.ExHentai;
    public IReadOnlyList<string> AcceptedInputItemTypes { get; } = [WorkflowItemTypes.SearchQuery];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Fixed;
    public string FixedOutputItemType => WorkflowItemTypes.ExHentaiGallery;

    public async Task<WorkflowItemOutcome> ProcessItemAsync(
        WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        if (item is not WorkflowQueryItem q || string.IsNullOrWhiteSpace(q.Query))
        {
            return WorkflowItemOutcome.DropItem;
        }

        var list = await _client.Search(new ExHentaiSearchRequestModel { Keyword = q.Query });
        var first = list.Resources?.FirstOrDefault();
        if (first is null)
        {
            ctx.Logger.LogInformation("No ExHentai result for query \"{Query}\"", q.Query);
            return WorkflowItemOutcome.DropItem;
        }

        return WorkflowItemOutcome.ReplaceWith(new SubscriptionItem(
            Id: first.Id.ToString(),
            Title: string.IsNullOrEmpty(first.Name) ? first.RawName : first.Name,
            Url: first.Url,
            ThumbnailUrl: first.CoverUrl));
    }
}
