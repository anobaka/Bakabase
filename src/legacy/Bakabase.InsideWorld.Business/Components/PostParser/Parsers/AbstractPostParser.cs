using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Parsers;

public abstract class AbstractPostParser : IPostParser
{
    protected readonly ILogger Logger;
    public abstract PostParserSource Source { get; }

    protected AbstractPostParser(ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType());
    }

    public abstract Task<PostParserTask> Parse(string link, CancellationToken ct);
}