using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// 第三方内容追踪控制器
/// 用于追踪用户在第三方网站上的浏览记录
/// </summary>
[Route("~/third-party-content-tracker")]
public class ThirdPartyContentTrackerController(IThirdPartyContentTrackerService service) : Controller
{
    /// <summary>
    /// 批量查询内容状态
    /// </summary>
    /// <param name="model">查询请求</param>
    /// <returns>内容状态列表</returns>
    [SwaggerOperation(OperationId = "QueryThirdPartyContentStatus")]
    [HttpPost("query")]
    public async Task<ListResponse<ThirdPartyContentTrackerStatusViewModel>> QueryStatus(
        [FromBody] ThirdPartyContentTrackerQueryInputModel model)
    {
        var result = await service.QueryContentStatus(
            model.DomainKey,
            model.Filter,
            model.ContentIds);

        return new ListResponse<ThirdPartyContentTrackerStatusViewModel>(result);
    }

    /// <summary>
    /// 批量标记内容为已查看
    /// </summary>
    /// <param name="model">标记请求</param>
    [SwaggerOperation(OperationId = "MarkThirdPartyContentAsViewed")]
    [HttpPost("mark-viewed")]
    public async Task<BaseResponse> MarkViewed(
        [FromBody] ThirdPartyContentTrackerMarkViewedInputModel model)
    {
        var contentItems = model.ContentItems
            .Select(c => (c.ContentId, c.UpdatedAt))
            .ToList();

        await service.MarkContentAsViewed(
            model.DomainKey,
            model.Filter,
            contentItems);

        return new BaseResponse();
    }

    /// <summary>
    /// 查找离指定内容最近的已查看内容
    /// </summary>
    /// <param name="domainKey">网站标识</param>
    /// <param name="filter">过滤规则</param>
    /// <param name="targetContentId">目标内容 ID</param>
    /// <returns>最近查看的内容</returns>
    [SwaggerOperation(OperationId = "FindNearestViewedContent")]
    [HttpGet("nearest-viewed")]
    public async Task<SingletonResponse<ThirdPartyContentTrackerNearestViewModel?>> FindNearestViewed(
        [FromQuery] string domainKey,
        [FromQuery] string? filter,
        [FromQuery] string targetContentId)
    {
        var result = await service.FindNearestViewedContent(domainKey, filter, targetContentId);
        return new SingletonResponse<ThirdPartyContentTrackerNearestViewModel?>(result);
    }
}
