using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Handlers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Services;

public class PostParserTaskService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, PostParserTaskDbModel, int> orm,
    IEnumerable<ISharedContentReader> readers,
    IEnumerable<ISharedContentPurchaser> purchasers,
    IEnumerable<IPostParseTargetHandler> handlers,
    IBOptions<SoulPlusOptions> soulPlusOptions,
    BTaskManager btm,
    IBakabaseLocalizer localizer,
    IHubContext<WebGuiHub, IWebGuiClient> uiHub) : IPostParserTaskService where TDbContext : DbContext
{
    private readonly ConcurrentDictionary<PostParserSource, ISharedContentReader> _fetcherMap =
        new(readers.Where(r => r.Source.HasValue).ToDictionary(d => d.Source!.Value, d => d));

    private readonly ConcurrentDictionary<PostParserSource, ISharedContentPurchaser> _purchaserMap =
        new(purchasers.ToDictionary(d => d.Source, d => d));

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
                    // Reset existing task: clear results and error, undelete, update targets
                    existing.Results = null;
                    existing.Error = null;
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

    public async Task ReParse(int id)
    {
        var task = await orm.GetByKey(id);
        if (task == null) return;
        task.Results = null;
        task.Error = null;
        task.IsDeleted = false;
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

            if (domainTask.Error != null)
            {
                result[link] = PostParserTaskStatus.Failed;
                continue;
            }

            if (IsPending(domainTask))
            {
                result[link] = PostParserTaskStatus.Pending;
                continue;
            }

            result[link] = PostParserTaskStatus.Complete;
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
        // Get tasks that are pending (no error, not deleted, have unparsed targets)
        var allTasks = (await orm.GetAll()).Select(d => d.ToDomainModel()).ToList();
        var pendingTasks = allTasks.Where(t => !t.IsDeleted && t.Error == null && IsPending(t)).ToList();

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
                        var content = await fetcher.ReadAsync(t.Link, ct);

                        // Reading stopped buying, so this page keeps doing it: same rule as before
                        // (anything dearer than the threshold stops the task), except that a lock
                        // with no stated price no longer compares as free and buys itself.
                        content = await UnlockForPageAsync(g.Key, fetcher, t.Link, content, ct);

                        t.Title ??= content.Title;

                        t.Results ??= new Dictionary<PostParseTarget, JsonNode?>();

                        foreach (var target in t.Targets)
                        {
                            // Skip already parsed targets
                            if (t.Results.ContainsKey(target))
                                continue;

                            if (!_handlerMap.TryGetValue(target, out var handler))
                                continue;

                            var handlerResult = await handler.HandleAsync(content, ct);
                            var jsonString = JsonSerializer.Serialize(handlerResult.Data, JsonSerializerOptions.Web);

                            if (!string.IsNullOrWhiteSpace(handlerResult.OptimizedTitle) &&
                                handlerResult.OptimizedTitle != t.Title)
                            {
                                t.Title = handlerResult.OptimizedTitle;
                            }

                            t.Results[target] = JsonNode.Parse(jsonString);
                        }
                    }
                    catch (Exception e)
                    {
                        t.Error = e.BuildFullInformationText();
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

    /// <summary>
    /// A task is pending if it has targets without results.
    /// </summary>
    private static bool IsPending(PostParserTask task)
    {
        if (task.Targets.Count == 0)
            return false;

        if (task.Results == null)
            return true;

        return task.Targets.Any(target => !task.Results.ContainsKey(target));
    }

    /// <summary>
    /// Buys what the post-parser page is allowed to buy and re-reads if anything changed. The
    /// acquisition pipeline does not use this: a step suspends and asks instead.
    /// </summary>
    private async Task<PostContent> UnlockForPageAsync(PostParserSource source, ISharedContentReader reader,
        string link, PostContent content, CancellationToken ct)
    {
        var locked = content.Locks.Unbought().ToList();

        if (locked.Count == 0 || !_purchaserMap.TryGetValue(source, out var purchaser))
        {
            return content;
        }

        var limit = soulPlusOptions.Value.AutoBuyThreshold;
        var tooDear = locked.FirstOrDefault(l => !l.IsWithin(limit));

        if (tooDear != null)
        {
            throw new Exception(tooDear.Price is { } price
                ? $"Failed due to price {price} is larger than auto buy threshold {limit}"
                : "Failed because a locked part of this post did not say what it costs");
        }

        foreach (var l in locked)
        {
            await purchaser.BuyAsync(l.Url!, ct);
        }

        return await reader.ReadAsync(link, ct);
    }

}
