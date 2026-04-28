using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.HealthScore.Abstractions.Components;
using Bakabase.Modules.HealthScore.Abstractions.Services;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Extensions;
using Bakabase.Modules.HealthScore.Models;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.HealthScore.Models.Input;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Services;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.HealthScore.Services;

public class HealthScoreService<TDbContext> :
    ResourceService<TDbContext, HealthScoreProfileDbModel, int>,
    IHealthScoreService
    where TDbContext : DbContext, IHealthScoreDbContext
{
    /// <summary>Stable id for the on-demand scoring task — see <see cref="TriggerRunNowSafe"/>.</summary>
    public const string ScoringTaskId = "HealthScoring";

    private readonly IPropertyService _propertyService;
    private readonly IResourceService _resourceService;
    private readonly IHealthScoreLocalizer _localizer;
    private readonly IBakabaseLocalizer _bakabaseLocalizer;
    private readonly IHealthScoreEngine _engine;
    private readonly HealthScoreProfileCache _cache;
    private readonly ResourceHealthScoreOrm<TDbContext> _orm;
    private readonly HealthScoreMembershipCountCache _membershipCountCache;
    private readonly IResourceSearchIndexService _searchIndex;
    private readonly BTaskManager _btaskManager;

    public HealthScoreService(
        IServiceProvider serviceProvider,
        IPropertyService propertyService,
        IResourceService resourceService,
        IHealthScoreLocalizer localizer,
        IBakabaseLocalizer bakabaseLocalizer,
        IHealthScoreEngine engine,
        HealthScoreProfileCache cache,
        ResourceHealthScoreOrm<TDbContext> orm,
        HealthScoreMembershipCountCache membershipCountCache,
        IResourceSearchIndexService searchIndex,
        BTaskManager btaskManager)
        : base(serviceProvider)
    {
        _propertyService = propertyService;
        _resourceService = resourceService;
        _localizer = localizer;
        _bakabaseLocalizer = bakabaseLocalizer;
        _engine = engine;
        _cache = cache;
        _orm = orm;
        _membershipCountCache = membershipCountCache;
        _searchIndex = searchIndex;
        _btaskManager = btaskManager;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private async Task<IReadOnlyDictionary<(PropertyPool, int), DomainProperty>> LoadPropertyMap()
    {
        var all = await _propertyService.GetProperties(PropertyPool.All);
        return all.ToDictionary(p => (p.Pool, p.Id), p => p);
    }

    public async Task<HealthScoreProfile?> Get(int id)
    {
        var db = await GetByKey(id);
        return db is null ? null : await db.ToDomainModelAsync(_propertyService);
    }

    public async Task<List<HealthScoreProfile>> GetAll()
    {
        var dbModels = await base.GetAll();
        var domain = new List<HealthScoreProfile>(dbModels.Count);
        foreach (var d in dbModels)
        {
            domain.Add(await d.ToDomainModelAsync(_propertyService));
        }

        return domain.OrderByDescending(p => p.Id).ToList();
    }

    public async Task<HealthScoreProfileDbModel?> GetDbModel(int id) => await GetByKey(id);

    public new async Task<List<HealthScoreProfileDbModel>> GetAllDbModels() =>
        (await base.GetAll()).OrderByDescending(p => p.Id).ToList();

    public async Task<HealthScoreProfileDbModel> Add()
    {
        var db = new HealthScoreProfileDbModel
        {
            Name = _localizer.DefaultProfileName(),
            Enabled = true,
            BaseScore = 100,
            Priority = 0,
            CreatedAt = DateTime.Now,
            RulesJson = "[]",
        };
        var added = await Add(db);
        var saved = added.Data ?? db;
        var domain = await saved.ToDomainModelAsync(_propertyService);
        _cache.Set(domain);
        return saved;
    }

    public async Task Patch(int id, HealthScoreProfilePatchInputModel model)
    {
        var existing = await GetByKey(id);
        if (existing is null) return;

        var oldHash = HealthScoreProfileHasher.Compute(existing);
        var hashAffectingChanged = false;

        var aggregationAffectingChanged = false;
        if (model.Name is not null) existing.Name = model.Name;
        if (model.Enabled is not null && model.Enabled.Value != existing.Enabled)
        {
            existing.Enabled = model.Enabled.Value;
            aggregationAffectingChanged = true;
        }
        if (model.Priority is not null && model.Priority.Value != existing.Priority)
        {
            existing.Priority = model.Priority.Value;
            aggregationAffectingChanged = true;
        }
        if (model.BaseScore is not null)
        {
            existing.BaseScore = model.BaseScore.Value;
            hashAffectingChanged = true;
        }

        if (model.MembershipFilter is not null)
        {
            existing.MembershipFilterJson = JsonSerializer.Serialize(model.MembershipFilter, JsonOptions);
            hashAffectingChanged = true;
        }

        if (model.Rules is not null)
        {
            // Input → DB is a structural copy: both shapes use string PropertyValue
            // (StandardValue-serialized), so JSON round-trip stays JsonElement-free.
            var ruleDbs = model.Rules.Select(r => r.ToDbModel()).ToList();
            existing.RulesJson = JsonSerializer.Serialize(ruleDbs, JsonOptions);
            hashAffectingChanged = true;
        }

        existing.UpdatedAt = DateTime.Now;
        var newHash = HealthScoreProfileHasher.Compute(existing);

        await Update(existing);

        // Refresh in-memory cache after the new shape is persisted.
        var refreshedDomain = await existing.ToDomainModelAsync(_propertyService);
        _cache.Set(refreshedDomain);

        if (hashAffectingChanged && oldHash != newHash)
        {
            // Existing rows for this profile become stale.
            await ClearProfileCache(id);
            await TriggerRunNowSafe();
        }
        else if (aggregationAffectingChanged)
        {
            // Score rows are still valid; only the aggregation result changes.
            var affected = _orm.GetResourceIdsForProfile(id);
            InvalidateIndex(affected);
        }
    }

    public async Task Delete(int id)
    {
        var affected = await _orm.RemoveByProfile(id);
        await RemoveByKey(id);
        _cache.Remove(id);
        _membershipCountCache.Clear(id);
        InvalidateIndex(affected);
    }

    public async Task Duplicate(int id)
    {
        var src = await GetByKey(id);
        if (src is null) return;
        var newName = await ComputeDuplicateName(src.Name);
        var clone = src with { Id = 0, Name = newName, CreatedAt = DateTime.Now, UpdatedAt = null };
        var added = await Add(clone);
        if (added.Data is not null)
        {
            var domain = await added.Data.ToDomainModelAsync(_propertyService);
            _cache.Set(domain);
        }
    }

    /// <summary>
    /// Pick a fresh name for a cloned profile by stripping any trailing
    /// <c>" #N"</c> from the source and appending the next free index across
    /// the whole profile set. <c>"Foo"</c> → <c>"Foo #1"</c>; <c>"Foo #3"</c>
    /// → <c>"Foo #4"</c> (or higher if "Foo #4" is already taken).
    /// </summary>
    private async Task<string> ComputeDuplicateName(string? srcName)
    {
        var trimmed = srcName ?? string.Empty;
        var trailing = SuffixPattern.Match(trimmed);
        var baseName = trailing.Success ? trimmed[..^trailing.Length] : trimmed;

        var existing = await base.GetAll();
        var siblingPattern = new Regex(
            $@"^{Regex.Escape(baseName)}\s+#(\d+)$",
            RegexOptions.CultureInvariant);
        var maxN = 0;
        foreach (var p in existing)
        {
            if (string.IsNullOrEmpty(p.Name)) continue;
            var m = siblingPattern.Match(p.Name);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > maxN) maxN = n;
        }
        return $"{baseName} #{maxN + 1}";
    }

    private static readonly Regex SuffixPattern = new(@"\s+#\d+$", RegexOptions.CultureInvariant);

    public Task<decimal?> GetAggregatedScore(int resourceId) =>
        Task.FromResult(ComputeAggregate(resourceId));

    public Task<Dictionary<int, decimal>> GetAggregatedScores(int[] resourceIds)
    {
        var result = new Dictionary<int, decimal>();
        if (resourceIds.Length == 0) return Task.FromResult(result);

        foreach (var rid in resourceIds)
        {
            var agg = ComputeAggregate(rid);
            if (agg.HasValue) result[rid] = agg.Value;
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// Same aggregation rule as <see cref="HealthScoreAggregateReader{TDbContext}"/> —
    /// duplicated here to avoid circular dependency between the service and the
    /// reader. Both read from the orm + profile cache, so they always agree.
    /// </summary>
    private decimal? ComputeAggregate(int resourceId)
    {
        var rows = _orm.GetByResource(resourceId);
        if (rows.Count == 0) return null;

        decimal? best = null;
        var bestPriority = int.MinValue;
        foreach (var row in rows)
        {
            var profile = _cache.Find(row.ProfileId);
            if (profile is null || !profile.Enabled) continue;

            if (best is null || profile.Priority > bestPriority)
            {
                bestPriority = profile.Priority;
                best = row.Score;
            }
            else if (profile.Priority == bestPriority && row.Score < best)
            {
                best = row.Score;
            }
        }
        return best;
    }

    public Task<List<ResourceHealthScoreDbModel>> GetRowsForResource(int resourceId) =>
        Task.FromResult(_orm.GetByResource(resourceId).ToList());

    public Task ClearProfileCache(int profileId) => _orm.ClearProfileHash(profileId);

    public Task ClearAllCaches() => _orm.ClearAllHashes();

    public Task RunNow() => TriggerRunNowSafe();

    private async Task TriggerRunNowSafe()
    {
        try
        {
            // Start(id, factory) ensures: if no task with this Id exists, the
            // factory enqueues a fresh one and starts it; if it exists in
            // {NotStarted, Error, Completed, Cancelled}, restart it; if Running,
            // no-op. The frontend reflects this lifecycle via the BTask store.
            await _btaskManager.Start(ScoringTaskId, BuildScoringTaskHandlerBuilder);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to trigger HealthScoring task via BTaskManager");
        }
    }

    private BTaskHandlerBuilder BuildScoringTaskHandlerBuilder() => new()
    {
        Id = ScoringTaskId,
        GetName = () => _bakabaseLocalizer.BTask_Name(ScoringTaskId),
        GetDescription = () => _bakabaseLocalizer.BTask_Description(ScoringTaskId),
        Type = BTaskType.Any,
        ResourceType = BTaskResourceType.Any,
        ConflictKeys = [ScoringTaskId],
        DependsOn = ["SearchIndex"],
        IsPersistent = true,
        DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
        Run = async args =>
        {
            await using var scope = args.RootServiceProvider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IHealthScoreService>();
            await service.RunScoring(
                async (percentage, process) =>
                {
                    await args.UpdateTask(t =>
                    {
                        t.Percentage = percentage;
                        t.Process = process;
                    });
                },
                args.CancellationToken);
        },
    };

    public async Task RunScoring(Func<int, string?, Task>? progress, CancellationToken ct)
    {
        var profiles = (await GetAll()).Where(p => p.Enabled).OrderBy(p => p.Id).ToList();
        _cache.Replace(profiles);

        if (profiles.Count == 0)
        {
            // Profiles all disabled / deleted. Aggregates the reader returns are now
            // null, but the search index still holds the old values until invalidated.
            var stale = _orm.GetAllScoredResourceIds();
            if (stale.Count > 0) _searchIndex.InvalidateResources(stale);
            await (progress?.Invoke(100, "0/0") ?? Task.CompletedTask);
            return;
        }

        var propByKey = await LoadPropertyMap();

        bool MatchProperty(PropertyPool pool, int pid, SearchOperation op, object? value, object? filterValue)
        {
            if (!propByKey.TryGetValue((pool, pid), out var property)) return false;
            var psh = PropertySystem.Property.TryGetSearchHandler(property.Type);
            return psh != null && psh.IsMatch(value, op, filterValue);
        }

        // Progress is split evenly across profiles. Within a profile the share is
        // weighted by how many resources we actually need to score, so the bar
        // moves smoothly even when one profile dominates the work.
        var touched = new HashSet<int>();
        var profileShare = 100.0 / profiles.Count;
        for (var i = 0; i < profiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var profile = profiles[i];
            var profileBasePct = i * profileShare;
            var profileLabel = $"{i + 1}/{profiles.Count} {profile.Name}";

            Task ReportSubProgress(int processed, int total)
            {
                var subFraction = total > 0 ? (double)processed / total : 1.0;
                var pct = (int)(profileBasePct + subFraction * profileShare);
                var process = total > 0
                    ? $"{profileLabel} ({processed}/{total})"
                    : profileLabel;
                return progress?.Invoke(pct, process) ?? Task.CompletedTask;
            }

            var profileTouched = await ProcessOneProfile(profile, MatchProperty, ReportSubProgress, ct);
            foreach (var id in profileTouched) touched.Add(id);
        }

        await (progress?.Invoke(100, $"{profiles.Count}/{profiles.Count}") ?? Task.CompletedTask);

        InvalidateIndex(touched);
    }

    /// <summary>
    /// The aggregate is recomputed on read (see <see cref="HealthScoreAggregateReader{TDbContext}"/>)
    /// from the orm + profile cache, so the only thing left to do after a write
    /// is poke the search index so it re-pulls the score for these resources.
    /// </summary>
    private void InvalidateIndex(IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count == 0) return;
        _searchIndex.InvalidateResources(resourceIds);
    }

    private async Task<HashSet<int>> ProcessOneProfile(
        HealthScoreProfile profile,
        Func<PropertyPool, int, SearchOperation, object?, object?, bool> matcher,
        Func<int, int, Task>? reportProgress,
        CancellationToken ct)
    {
        // 1. Membership: empty filter == all resources.
        var search = new ResourceSearch
        {
            Group = profile.MembershipFilter,
            PageSize = int.MaxValue,
            PageIndex = 1,
        };
        var memberIds = (await _resourceService.GetAllIds(search)).ToHashSet();
        _membershipCountCache.Set(profile.Id, memberIds.Count);

        // 2. Drop rows for resources that left membership. They lose this profile's
        // contribution to their aggregate, so flag them as touched.
        var touched = await _orm.RemoveByProfileWhereResourceNotIn(profile.Id, memberIds, ct);

        if (memberIds.Count == 0)
        {
            await (reportProgress?.Invoke(0, 0) ?? Task.CompletedTask);
            return touched;
        }

        // 3. Skip-already-scored: resources whose stored hash matches current hash.
        var currentHash = profile.ProfileHash ?? string.Empty;
        var freshIds = _orm.GetResourceIdsForProfile(profile.Id, currentHash);

        var toScore = memberIds.Where(id => !freshIds.Contains(id)).ToList();
        if (toScore.Count == 0)
        {
            await (reportProgress?.Invoke(0, 0) ?? Task.CompletedTask);
            return touched;
        }

        // Announce baseline before any batch runs so the UI can swap from the
        // previous profile's label as soon as we start.
        await (reportProgress?.Invoke(0, toScore.Count) ?? Task.CompletedTask);

        // 4. Score in batches to keep memory bounded.
        const int batchSize = 50;
        var scored = 0;
        for (var offset = 0; offset < toScore.Count; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = toScore.Skip(offset).Take(batchSize).ToArray();
            var resources = await _resourceService.GetByKeys(batch);

            var pendingRows = new List<ResourceHealthScoreDbModel>(resources.Count);

            foreach (var resource in resources)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(resource.Path)) continue;

                var snapshot = new ResourceFsSnapshot(resource.Path, resource.Covers);

                List<object?>? GetPropertyValues(PropertyPool pool, int pid)
                {
                    var poolMap = resource.Properties?.GetValueOrDefault((int)pool);
                    var prop = poolMap?.GetValueOrDefault(pid);
                    return prop?.Values?.Select(v => v.Value).ToList();
                }

                var ctxEval = new ResourceMatcherEvaluationContext
                {
                    Snapshot = snapshot,
                    GetPropertyValues = GetPropertyValues,
                    PropertyValueMatcher = matcher,
                };

                var result = _engine.Evaluate(profile, ctxEval);

                pendingRows.Add(new ResourceHealthScoreDbModel
                {
                    ResourceId = resource.Id,
                    ProfileId = profile.Id,
                    Score = result.Score,
                    ProfileHash = currentHash,
                    MatchedRulesJson = result.MatchedRules.Count == 0
                        ? null
                        : JsonSerializer.Serialize(result.MatchedRules, JsonOptions),
                    EvaluatedAt = DateTime.Now,
                });

                touched.Add(resource.Id);
            }

            if (pendingRows.Count > 0) await _orm.UpsertBatch(pendingRows, ct);
            scored = Math.Min(offset + batch.Length, toScore.Count);
            await (reportProgress?.Invoke(scored, toScore.Count) ?? Task.CompletedTask);
        }

        return touched;
    }
}
