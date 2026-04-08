using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

public interface IPostContentFetcher
{
    PostParserSource Source { get; }
    Task<PostContent> FetchAsync(string link, CancellationToken ct);
}
