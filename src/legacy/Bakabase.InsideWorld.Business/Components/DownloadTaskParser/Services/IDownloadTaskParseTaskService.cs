using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using Bootstrap.Components.Tasks;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Services;

public interface IDownloadTaskParseTaskService
{
    Task<List<DownloadTaskParseTask>> GetAll();
    Task AddRange(Dictionary<DownloadTaskParserSource, List<string>> sourceLinksMap);
    Task Delete(int id);
    Task DeleteAll();
    Task Put(int id, DownloadTaskParseTask pdt);
    Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt, CancellationToken ct);
}