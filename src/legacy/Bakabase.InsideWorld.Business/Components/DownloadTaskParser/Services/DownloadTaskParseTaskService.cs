using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Extensions;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Parsers;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Services;

public class DownloadTaskParseTaskService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, DownloadTaskParseTaskDbModel, int> orm,
    IEnumerable<IDownloadTaskParser> parsers,
    BTaskManager btm,
    IBakabaseLocalizer localizer) : IDownloadTaskParseTaskService where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<DownloadTaskParserSource, IDownloadTaskParser> _parserMap =
        new(parsers.ToDictionary(d => d.Source, d => d));

    public async Task<List<DownloadTaskParseTask>> GetAll()
    {
        return (await orm.GetAll()).Select(d => d.ToDomainModel()).ToList();
    }

    public async Task AddRange(Dictionary<DownloadTaskParserSource, List<string>> sourceLinksMap)
    {
        var tasks = new List<DownloadTaskParseTaskDbModel>();
        foreach (var (source, links) in sourceLinksMap)
        {
            tasks.AddRange(links.Select(link => new DownloadTaskParseTaskDbModel
            {
                Source = source,
                Link = link,
            }));
        }

        await orm.AddRange(tasks);
    }

    public async Task Delete(int id)
    {
        await orm.RemoveByKey(id);
    }

    public async Task DeleteAll()
    {
        var data = await orm.GetAll();
        await orm.RemoveRange(data);
    }

    public async Task Put(int id, DownloadTaskParseTask pdt)
    {
        var dbModel = (pdt with {Id = id}).ToDbModel();
        await orm.Update(dbModel);
    }

    public async Task ParseAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct)
    {
        var pendingData = await orm.GetAll(x => !x.ParsedAt.HasValue && x.Error == null);
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
                    await pt.WaitWhilePausedAsync();
                    ct.ThrowIfCancellationRequested();
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

                    await orm.Update(data.ToDbModel());
                    Interlocked.Increment(ref doneCount);
                    if (onProgress != null)
                    {
                        var pp = (int)(100m * (doneCount - 1) / totalCount);
                        var np = (int)(100m * doneCount / totalCount);
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