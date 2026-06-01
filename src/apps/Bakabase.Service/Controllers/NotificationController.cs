using System.Threading.Tasks;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Models.View;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("notification")]
public class NotificationController(INotificationService service) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "SearchNotifications")]
    public async Task<SearchResponse<NotificationViewModel>> Search([FromQuery] NotificationSearchInputModel model)
    {
        var result = await service.SearchAsync(model);
        return new SearchResponse<NotificationViewModel>(
            result.Data!.Select(NotificationViewModel.From),
            result.TotalCount,
            result.PageIndex,
            result.PageSize);
    }

    [HttpGet("unread-count")]
    [SwaggerOperation(OperationId = "GetUnreadNotificationCount")]
    public async Task<SingletonResponse<int>> GetUnreadCount()
    {
        // Object initializer instead of `new SingletonResponse<int>(count)` — when T=int
        // overload resolution picks the (int code) error-response ctor over (T? data),
        // so the count ends up as an HTTP status code with Data=0.
        return new SingletonResponse<int> { Data = await service.GetUnreadCountAsync() };
    }

    [HttpPost("mark-read")]
    [SwaggerOperation(OperationId = "MarkNotificationsAsRead")]
    public async Task<BaseResponse> MarkAsRead([FromBody] MarkNotificationsAsReadInputModel model)
    {
        await service.MarkAsReadAsync(model.Ids);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete]
    [SwaggerOperation(OperationId = "DeleteNotifications")]
    public async Task<BaseResponse> Delete([FromBody] DeleteNotificationsInputModel model)
    {
        await service.DeleteAsync(model.Ids);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("clear-read")]
    [SwaggerOperation(OperationId = "ClearReadNotifications")]
    public async Task<BaseResponse> ClearRead()
    {
        await service.ClearReadAsync();
        return BaseResponseBuilder.Ok;
    }

    /// <summary>Diagnostic endpoint — synthesizes one persistent notification so the
    /// frontend toast / badge / center wiring can be exercised end-to-end.</summary>
    [HttpPost("test")]
    [SwaggerOperation(OperationId = "CreateTestNotification")]
    public async Task<BaseResponse> CreateTest([FromBody] CreateTestNotificationInputModel model)
    {
        await service.CreateAsync(new NotificationCreationInputModel
        {
            Source = "test",
            Title = string.IsNullOrWhiteSpace(model.Title) ? "Test notification" : model.Title!,
            Body = model.Body,
            Severity = model.Severity,
        });
        return BaseResponseBuilder.Ok;
    }
}
