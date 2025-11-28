using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// 第三方内容追踪服务接口
/// </summary>
public interface IThirdPartyContentTrackerService
{
    /// <summary>
    /// 批量查询内容状态
    /// </summary>
    /// <param name="domainKey">网站标识</param>
    /// <param name="filter">过滤规则（可选）</param>
    /// <param name="contentIds">内容 ID 列表</param>
    /// <returns>内容状态列表</returns>
    Task<List<ThirdPartyContentTrackerStatusViewModel>> QueryContentStatus(
        string domainKey,
        string? filter,
        List<string> contentIds);

    /// <summary>
    /// 批量标记内容为已查看
    /// </summary>
    /// <param name="domainKey">网站标识</param>
    /// <param name="filter">过滤规则（可选）</param>
    /// <param name="contentItems">内容列表（包含 ID 和更新时间）</param>
    Task MarkContentAsViewed(
        string domainKey,
        string? filter,
        List<(string contentId, DateTime? updatedAt)> contentItems);

    /// <summary>
    /// 查找离指定内容最近的已查看内容
    /// </summary>
    /// <param name="domainKey">网站标识</param>
    /// <param name="filter">过滤规则（可选）</param>
    /// <param name="targetContentId">目标内容 ID</param>
    /// <returns>最近查看的内容（如果存在）</returns>
    Task<ThirdPartyContentTrackerNearestViewModel?> FindNearestViewedContent(
        string domainKey,
        string? filter,
        string targetContentId);
}
