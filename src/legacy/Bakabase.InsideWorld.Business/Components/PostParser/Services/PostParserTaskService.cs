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
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Handlers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public class PostParserTaskService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int> orm,
    IEnumerable<IPostContentFetcher> fetchers,
    IEnumerable<IPostParseTargetHandler> handlers,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub) : IPostParserTaskService where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<PostParserSource, IPostContentFetcher> _fetcherMap =
        new(fetchers.ToDictionary(d => d.Source, d => d));

    private readonly ConcurrentDictionary<PostParseTarget, IPostParseTargetHandler> _handlerMap =
        new(handlers.ToDictionary(d => d.Target, d => d));

    public async Task<List<PostParserTask>> GetAll()
    {
        return (await orm.GetAll()).Select(d => d.ToDomainModel()).ToList();
    }

    public async Task AddRange(Dictionary<PostParserSource, List<string>> sourceLinksMap,
        List<PostParseTarget> targets)
    {
        var tasks = new List<PostParserTaskDbModel>();
        foreach (var (source, links) in sourceLinksMap)
        {
            tasks.AddRange(links.Select(link => new PostParserTaskDbModel
            {
                Source = source,
                Link = link,
                Targets = Newtonsoft.Json.JsonConvert.SerializeObject(targets),
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
        // Get tasks that have unparsed targets
        var allTasks = (await orm.GetAll()).Select(d => d.ToDomainModel()).ToList();
        var pendingTasks = allTasks.Where(t => HasUnparsedTargets(t)).ToList();

        if (pendingTasks.Count == 0)
            return;

        var groups = pendingTasks.GroupBy(d => d.Source)
            .ToDictionary(d => d.Key, d => d.ToList());
        var runningTasks = new List<Task>();
        var totalCount = pendingTasks.Count;
        var doneCount = 0;

        foreach (var g in groups)
        {
            if (!_fetcherMap.TryGetValue(g.Key, out var fetcher))
                continue;

            runningTasks.Add(Task.Run((Func<Task>) StartBySource, ct));
            continue;

            async Task StartBySource()
            {
                foreach (var t in g.Value)
                {
                    await pt.WaitWhilePausedAsync(ct);

                    try
                    {
                        var content = await fetcher.FetchAsync(t.Link, ct);
                        t.Title ??= content.Title;

                        t.Results ??= new Dictionary<PostParseTarget, PostParseTargetResult>();

                        foreach (var target in t.Targets)
                        {
                            // Skip already parsed targets
                            if (t.Results.TryGetValue(target, out var existing) && existing.ParsedAt.HasValue)
                                continue;

                            if (!_handlerMap.TryGetValue(target, out var handler))
                                continue;

                            try
                            {
                                var data = await handler.HandleAsync(content, ct);
                                t.Results[target] = new PostParseTargetResult
                                {
                                    Data = data,
                                    ParsedAt = DateTime.Now,
                                };
                            }
                            catch (Exception ex)
                            {
                                t.Results[target] = new PostParseTargetResult
                                {
                                    Error = ex.BuildFullInformationText(),
                                };
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Content fetching failed - mark all targets as failed
                        t.Results ??= new Dictionary<PostParseTarget, PostParseTargetResult>();
                        foreach (var target in t.Targets)
                        {
                            if (t.Results.TryGetValue(target, out var existing) && existing.ParsedAt.HasValue)
                                continue;

                            t.Results[target] = new PostParseTargetResult
                            {
                                Error = e.BuildFullInformationText(),
                            };
                        }
                    }

                    await Put(t.Id, t);
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

        await Task.WhenAll(runningTasks);
    }

    private static bool HasUnparsedTargets(PostParserTask task)
    {
        if (task.Targets.Count == 0)
            return false;

        if (task.Results == null)
            return true;

        return task.Targets.Any(target =>
            !task.Results.TryGetValue(target, out var result) || !result.ParsedAt.HasValue);
    }
}
