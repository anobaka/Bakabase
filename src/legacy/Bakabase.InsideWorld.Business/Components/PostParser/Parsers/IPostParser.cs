using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Parsers;

public interface IPostParser
{
    PostParserSource Source { get; }
    Task<PostParserTask> Parse(string link, CancellationToken ct);
}