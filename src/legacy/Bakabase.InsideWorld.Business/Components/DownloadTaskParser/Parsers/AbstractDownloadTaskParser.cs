using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Services;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Parsers;

public abstract class AbstractDownloadTaskParser: IDownloadTaskParser
{
    public abstract DownloadTaskParserSource Source { get; }
    public abstract Task<DownloadTaskParseTask> Parse(string link);
}