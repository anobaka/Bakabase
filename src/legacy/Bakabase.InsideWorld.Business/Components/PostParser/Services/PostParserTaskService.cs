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
        var allExisting = await orm.GetAll();
        var newTasks = new List<PostParserTaskDbModel>();
        var updatedTasks = new List<PostParserTaskDbModel>();

        foreach (var (source, links) in sourceLinksMap)
        {
            foreach (var link in links)
            {
                var existing = allExisting.FirstOrDefault(t => t.Source == source && t.Link == link);
                if (existing != null)
                {
                    // Reset existing task: clear results, undelete, update targets
                    existing.Results = null;
                    existing.IsDeleted = false;
                    existing.Targets = Newtonsoft.Json.JsonConvert.SerializeObject(targets);
                    updatedTasks.Add(existing);
                }
                else
                {
                    newTasks.Add(new PostParserTaskDbModel
                    {
                        Source = source,
                        Link = link,
                        Targets = Newtonsoft.Json.JsonConvert.SerializeObject(targets),
                    });
                }
            }
        }

        if (newTasks.Count > 0)
            await orm.AddRange(newTasks);
        if (updatedTasks.Count > 0)
            await orm.UpdateRange(updatedTasks);

        foreach (var task in newTasks.Concat(updatedTasks))
        {
            await uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), task.ToDomainModel());
        }
    }

    public async Task Delete(int id)
    {
        var task = await orm.GetByKey(id);
        if (task == null) return;
        task.IsDeleted = true;
        await orm.Update(task);
        await uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), task.ToDomainModel());
    }

    public async Task DeleteAll()
    {
        var data = await orm.GetAll();
        foreach (var task in data)
        {
            task.IsDeleted = true;
        }
        await orm.UpdateRange(data);
        foreach (var task in data)
        {
            await uiHub.Clients.All.GetIncrementalData(nameof(PostParserTask), task.ToDomainModel());
        }
    }

    public async Task<Dictionary<string, PostParserTaskStatus>> GetStatusesByLinks(PostParserSource source, List<string> links)
    {
        var allTasks = await orm.GetAll();
        var result = new Dictionary<string, PostParserTaskStatus>();

        foreach (var link in links)
        {
            var task = allTasks.FirstOrDefault(t => t.Source == source && t.Link == link);
            if (task == null)
            {
                result[link] = PostParserTaskStatus.None;
                continue;
            }

            if (task.IsDeleted)
            {
                result[link] = PostParserTaskStatus.Deleted;
                continue;
            }

            var domainTask = task.ToDomainModel();
            if (domainTask.Targets.Count == 0 || domainTask.Results == null)
            {
                result[link] = PostParserTaskStatus.Pending;
                continue;
            }

            var allTargetsDone = domainTask.Targets.All(target =>
                domainTask.Results.TryGetValue(target, out var r) && r.ParsedAt.HasValue);

            if (!allTargetsDone)
            {
                result[link] = PostParserTaskStatus.Pending;
                continue;
            }

            var anyError = domainTask.Results.Values.Any(r => r.Error != null);
            result[link] = anyError ? PostParserTaskStatus.Failed : PostParserTaskStatus.Complete;
        }

        return result;
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
        var pendingTasks = allTasks.Where(t => !t.IsDeleted && HasUnparsedTargets(t)).ToList();

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
