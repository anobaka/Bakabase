using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Input;
using Bakabase.Modules.Subscription.Abstractions.Models.View;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("subscription")]
public class SubscriptionController(
    ISubscriptionService service,
    ISubscriptionProviderRegistry providers) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "SearchSubscriptions")]
    public async Task<ListResponse<SubscriptionViewModel>> Search([FromQuery] SubscriptionSearchInputModel model)
    {
        var rows = await service.SearchAsync(model);
        return new ListResponse<SubscriptionViewModel>(rows.Select(r =>
        {
            var summary = providers.TryGet(r.Kind, out var p) ? p.DescribeTarget(r.TargetJson) : null;
            return SubscriptionViewModel.From(r, summary);
        }));
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetSubscription")]
    public async Task<SingletonResponse<SubscriptionViewModel?>> Get(int id)
    {
        var row = await service.GetAsync(id);
        if (row is null) return new SingletonResponse<SubscriptionViewModel?>(null);
        var summary = providers.TryGet(row.Kind, out var p) ? p.DescribeTarget(row.TargetJson) : null;
        return new SingletonResponse<SubscriptionViewModel?>(SubscriptionViewModel.From(row, summary));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddSubscription")]
    public async Task<SingletonResponse<SubscriptionViewModel>> Add([FromBody] SubscriptionCreationInputModel model, CancellationToken ct)
    {
        var row = await service.CreateAsync(model, ct);
        var summary = providers.TryGet(row.Kind, out var p) ? p.DescribeTarget(row.TargetJson) : null;
        return new SingletonResponse<SubscriptionViewModel>(SubscriptionViewModel.From(row, summary));
    }

    [HttpPatch("{id:int}")]
    [SwaggerOperation(OperationId = "PatchSubscription")]
    public async Task<SingletonResponse<SubscriptionViewModel>> Patch(int id, [FromBody] SubscriptionUpdateInputModel model, CancellationToken ct)
    {
        var row = await service.UpdateAsync(id, model, ct);
        var summary = providers.TryGet(row.Kind, out var p) ? p.DescribeTarget(row.TargetJson) : null;
        return new SingletonResponse<SubscriptionViewModel>(SubscriptionViewModel.From(row, summary));
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteSubscription")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.DeleteAsync(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/run")]
    [SwaggerOperation(OperationId = "RunSubscriptionCheck")]
    public async Task<SingletonResponse<SubscriptionCheckSummaryViewModel?>> RunNow(int id, CancellationToken ct)
    {
        var summary = await service.RunCheckAsync(id, ct);
        if (summary is null) return new SingletonResponse<SubscriptionCheckSummaryViewModel?>(null);
        return new SingletonResponse<SubscriptionCheckSummaryViewModel?>(new SubscriptionCheckSummaryViewModel
        {
            FirstRun = summary.FirstRun,
            NewItemCount = summary.NewItemCount,
            UpdatedItemCount = summary.UpdatedItemCount,
            Error = summary.Error,
        });
    }

    [HttpGet("providers")]
    [SwaggerOperation(OperationId = "GetSubscriptionProviders")]
    public ListResponse<SubscriptionProviderViewModel> GetProviders()
    {
        return new ListResponse<SubscriptionProviderViewModel>(providers.All.Select(p => new SubscriptionProviderViewModel
        {
            Kind = p.Kind,
            DisplayName = p.DisplayName,
            Icon = p.Icon,
        }));
    }
}
