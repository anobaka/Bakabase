using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Handlers;

/// <summary>Keeps the existing task result shape while extraction is shared with other callers.</summary>
public class DownloadInfoHandler(IPostDownloadInfoExtractor extractor) : IPostParseTargetHandler
{
    public PostParseTarget Target => PostParseTarget.DownloadInfo;

    public async Task<PostParseHandlerResult> HandleAsync(PostContent content, CancellationToken ct)
    {
        var result = await extractor.ExtractAsync(LegacyPostContentService.ToContent(content), ct);
        var resources = result.Resources.Select(r => new DownloadInfoResource
        {
            Link = r.Link,
            Code = r.Code,
            Password = r.Password,
            DriveKind = AcquisitionDriveKinds.Infer(r.Link)
        }).ToList();
        return new PostParseHandlerResult(new {resources = resources.Count == 0 ? null : resources}, result.Title);
    }
}
