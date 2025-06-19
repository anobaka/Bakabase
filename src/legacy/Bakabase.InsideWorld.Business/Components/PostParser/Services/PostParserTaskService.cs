using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Parsers;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public class PostParserTaskService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int> orm,
    IEnumerable<IPostParser> parsers,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub) : IPostParserTaskService where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<PostParserSource, IPostParser> _parserMap =
        new(parsers.ToDictionary(d => d.Source, d => d));

    public async Task<List<PostParserTask>> GetAll()
    {
        return (await orm.GetAll()).Select(d => d.ToDomainModel()).ToList();
    }

    public async Task AddRange(Dictionary<PostParserSource, List<string>> sourceLinksMap)
    {
        var tasks = new List<PostParserTaskDbModel>();
        foreach (var (source, links) in sourceLinksMap)
        {
            tasks.AddRange(links.Select(link => new PostParserTaskDbModel
            {
                Source = source,
                Link = link,
            }));
        }

        await orm.AddRange(tasks);

        foreach (var task in tasks)
        {
            await uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), task.ToDomainModel());
        }
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
        await uiHub.Clients.All.DeleteData(nameof(PostParserTask), id);
    }

    public async Task DeleteAll()
    {
        var data = await orm.GetAll();
        await orm.RemoveRange(data);
        await uiHub.Clients.All.DeleteAllData(nameof(PostParserTask));
    }

    public async Task Put(int id, PostParserTask pdt)
    {
        var dbModel = (pdt with {Id = id}).ToDbModel();
        await orm.Update(dbModel);
        await uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), dbModel.ToDomainModel());
    }

    public async Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct)
    {
        var pendingData = await orm.GetAll(x => !x.ParsedAt.HasValue);
        var groups = pendingData.GroupBy(d => d.Source)
            .ToDictionary(d => d.Key, d => d.Select(c => c.ToDomainModel()).ToList());
        var tasks = new List<Task>();
        var totalCount = pendingData.Count;
        var doneCount = 0;
        foreach (var g in groups)
        {
            var parser = _parserMap[g.Key];
            tasks.Add(Task.Run((Func<Task>) StartBySource, ct));
            continue;

            async Task StartBySource()
            {
                foreach (var t in g.Value)
                {
                    await pt.WaitWhilePausedAsync(ct);
                    var data = t;
                    try
                    {
                        data = await parser.Parse(data.Link, ct);
                        data.Id = t.Id;
                    }
                    catch (Exception e)
                    {
                        t.Error = e.BuildFullInformationText();
                    }

                    await Put(t.Id, data);
                    Interlocked.Increment(ref doneCount);
                    if (onProgress != null)
                    {
                        var pp = (int) (100m * (doneCount - 1) / totalCount);
                        var np = (int) (100m * doneCount / totalCount);
                        if (np != pp)
                        {
                            await onProgress(np);
                        }
                    }
                }
            }
        }

        await Task.WhenAll(tasks);
    }
}