using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Parsers;

public interface IDownloadTaskParser
{
    DownloadTaskParserSource Source { get; }
    Task<DownloadTaskParseTask> Parse(string link, CancellationToken ct);
}