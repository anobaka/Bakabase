using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Tasks;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public interface IPostParserTaskService
{
    Task<List<PostParserTask>> GetAll();
    Task AddRange(Dictionary<PostParserSource, List<string>> sourceLinksMap);
    Task Delete(int id);
    Task DeleteAll();
    Task Put(int id, PostParserTask pdt);
    Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt, CancellationToken ct);
}