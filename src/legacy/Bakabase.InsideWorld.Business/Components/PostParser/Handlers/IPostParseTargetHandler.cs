using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Handlers;

public interface IPostParseTargetHandler
{
    PostParseTarget Target { get; }
    Task<object> HandleAsync(PostContent content, CancellationToken ct);
}
