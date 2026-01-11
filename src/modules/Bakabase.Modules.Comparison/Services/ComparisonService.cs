using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Comparison.Abstractions.Models;
using Bakabase.Modules.Comparison.Abstractions.Services;
using Bakabase.Modules.Comparison.Components;
using Bakabase.Modules.Comparison.Components.Strategies;
using Bakabase.Modules.Comparison.Extensions;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Comparison.Models.Domain;
using Bakabase.Modules.Comparison.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Input;
using Bakabase.Modules.Comparison.Models.View;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Extensions;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Comparison.Services;

public class ComparisonService<TDbContext> : ResourceService<TDbContext, ComparisonPlanDbModel, int>, IComparisonService
    where TDbContext : DbContext, IComparisonDbContext
{
    private readonly ResourceService<TDbContext, ComparisonRuleDbModel, int> _ruleOrm;
    private readonly ResourceService<TDbContext, ComparisonResultGroupDbModel, int> _groupOrm;
    private readonly ResourceService<TDbContext, ComparisonResultGroupMemberDbModel, int> _memberOrm;
    private readonly ResourceService<TDbContext, ComparisonResultPairDbModel, int> _pairOrm;
    private readonly ISpecialTextService _specialTextService;
    private readonly IPropertyService _propertyService;
    private readonly BTaskManager _taskManager;
    private readonly Dictionary<ComparisonMode, IComparisonStrategy> _strategies;

    public ComparisonService(
        IServiceProvider serviceProvider,
        ResourceService<TDbContext, ComparisonRuleDbModel, int> ruleOrm,
        ResourceService<TDbContext, ComparisonResultGroupDbModel, int> groupOrm,
        ResourceService<TDbContext, ComparisonResultGroupMemberDbModel, int> memberOrm,
        ResourceService<TDbContext, ComparisonResultPairDbModel, int> pairOrm,
        ISpecialTextService specialTextService,
        IPropertyService propertyService,
        BTaskManager taskManager,
        IEnumerable<IComparisonStrategy> strategies)
        : base(serviceProvider)
    {
        _ruleOrm = ruleOrm;
        _groupOrm = groupOrm;
        _memberOrm = memberOrm;
        _pairOrm = pairOrm;
        _specialTextService = specialTextService;
        _propertyService = propertyService;
        _taskManager = taskManager;
        _strategies = strategies.ToDictionary(s => s.Mode);
    }

    #region Plan CRUD

    public async Task<(ComparisonPlanDbModel Plan, List<ComparisonRuleDbModel> Rules, int GroupCount)?> GetPlanDbModelAsync(int id)
    {
        var plan = await GetByKey(id);
        if (plan == null) return null;

        var rules = await _ruleOrm.GetAll(r => r.PlanId == id);
        var groupCount = await _groupOrm.Count(g => g.PlanId == id);

        return (plan, rules, groupCount);
    }

    public async Task<List<(ComparisonPlanDbModel Plan, List<ComparisonRuleDbModel> Rules, int GroupCount)>> GetAllPlanDbModelsAsync()
    {
        var plans = await GetAll();
        var allRules = await _ruleOrm.GetAll();
        var groupCounts = await DbContext.ComparisonResultGroups
            .GroupBy(g => g.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PlanId, g => g.Count);

        var result = new List<(ComparisonPlanDbModel Plan, List<ComparisonRuleDbModel> Rules, int GroupCount)>();
        foreach (var p in plans)
        {
            var rules = allRules.Where(r => r.PlanId == p.Id).ToList();
            groupCounts.TryGetValue(p.Id, out var count);
            result.Add((p, rules, count));
        }
        return result.OrderByDescending(p => p.Plan.Id).ToList();
    }

    public async Task<ComparisonPlan> CreatePlanAsync(ComparisonPlanCreateInputModel input)
    {
        var dbModel = new ComparisonPlanDbModel
        {
            Name = input.Name,
            SearchJson = input.Search?.ToDbModel().ToJson(),
            Threshold = input.Threshold,
            CreatedAt = DateTime.Now
        };

        await Add(dbModel);

        // 添加规则
        var rules = input.Rules.Select((r, i) => r.ToDbModel(dbModel.Id, i)).ToList();
        await _ruleOrm.AddRange(rules);

        return await dbModel.ToDomainModel(rules.Select(r => r.ToDomainModel()).ToList(), _propertyService);
    }

    public async Task UpdatePlanAsync(int id, ComparisonPlanPatchInputModel input)
    {
        var plan = await GetByKey(id);
        if (plan == null) return;

        if (input.Name != null) plan.Name = input.Name;
        if (input.Search != null) plan.SearchJson = input.Search.ToDbModel().ToJson();
        if (input.Threshold.HasValue) plan.Threshold = input.Threshold.Value;

        await Update(plan);

        // 更新规则（全量替换）
        if (input.Rules != null)
        {
            await _ruleOrm.RemoveAll(r => r.PlanId == id);
            var rules = input.Rules.Select((r, i) => r.ToDbModel(id, i)).ToList();
            await _ruleOrm.AddRange(rules);
        }
    }

    public async Task DeletePlanAsync(int id)
    {
        // 删除相关数据
        await ClearResultsAsync(id);
        await _ruleOrm.RemoveAll(r => r.PlanId == id);
        await RemoveByKey(id);
    }

    public async Task<ComparisonPlan> DuplicatePlanAsync(int id)
    {
        var plan = await GetByKey(id);
        if (plan == null)
            throw new ArgumentException($"Plan {id} not found");

        var rules = await _ruleOrm.GetAll(r => r.PlanId == id);

        var newPlan = new ComparisonPlanDbModel
        {
            Name = $"{plan.Name} (Copy)",
            SearchJson = plan.SearchJson,
            Threshold = plan.Threshold,
            CreatedAt = DateTime.Now
        };
        await Add(newPlan);

        var newRules = rules.Select(r => new ComparisonRuleDbModel
        {
            PlanId = newPlan.Id,
            Order = r.Order,
            PropertyPool = r.PropertyPool,
            PropertyId = r.PropertyId,
            PropertyValueScope = r.PropertyValueScope,
            Mode = r.Mode,
            Parameter = r.Parameter,
            Normalize = r.Normalize,
            Weight = r.Weight,
            IsVeto = r.IsVeto,
            VetoThreshold = r.VetoThreshold,
            OneNullBehavior = r.OneNullBehavior,
            BothNullBehavior = r.BothNullBehavior
        }).ToList();
        await _ruleOrm.AddRange(newRules);

        return await newPlan.ToDomainModel(newRules.Select(r => r.ToDomainModel()).ToList(), _propertyService);
    }

    #endregion

    #region Comparison Execution

    public async Task<string> StartComparisonTaskAsync(int planId)
    {
        var plan = await GetByKey(planId);
        if (plan == null)
            throw new ArgumentException($"Plan {planId} not found");

        var taskId = $"Comparison:{planId}";
        var conflictKey = $"Comparison:{planId}";

        var handler = new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => $"对比任务: {plan.Name}",
            GetDescription = () => $"正在执行对比方案 [{plan.Name}]",
            ConflictKeys = [conflictKey],
            IsPersistent = false,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any, 
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IComparisonService>();
                await service.ExecuteComparisonAsync(
                    planId,
                    async (percentage, process) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = percentage;
                            t.Process = process;
                        });
                    },
                    args.PauseToken,
                    args.CancellationToken);
            }
        };

        await _taskManager.Enqueue(handler);
        return taskId;
    }

    public async Task ExecuteComparisonAsync(int planId, Func<int, string, Task> progressCallback, PauseToken pt, CancellationToken ct)
    {
        // 1. 加载方案
        var planDb = await GetByKey(planId);
        if (planDb == null)
            throw new ArgumentException($"Plan {planId} not found");

        var rulesDb = await _ruleOrm.GetAll(r => r.PlanId == planId);
        var plan = await planDb.ToDomainModel(rulesDb.Select(r => r.ToDomainModel()).ToList(), _propertyService);

        // 2. 清除旧结果
        await ClearResultsAsync(planId);

        // 3. 获取资源列表
        var resourceService = GetRequiredService<IResourceService>();
        var resources = await GetFilteredResourcesAsync(plan.Search, resourceService, ct);

        if (resources.Count < 2)
        {
            // 没有足够的资源进行比对
            await UpdateLastRunAtAsync(planId);
            return;
        }

        // 4. 初始化缓存上下文
        using var context = new ComparisonContext(_specialTextService);

        // 5. 计算相似度矩阵
        var pairs = new List<(int r1, int r2, double score, List<RuleScoreDetail> details)>();
        var total = resources.Count * (resources.Count - 1) / 2;
        var processed = 0;

        for (var i = 0; i < resources.Count; i++)
        {
            for (var j = i + 1; j < resources.Count; j++)
            {
                ct.ThrowIfCancellationRequested();
                await pt.WaitWhilePausedAsync(ct);

                var (score, details) = CalculateSimilarityWithDetails(resources[i], resources[j], plan, rulesDb, context);
                if (score >= plan.Threshold)
                {
                    pairs.Add((resources[i].Id, resources[j].Id, score, details));
                }

                processed++;
                if (processed % 100 == 0 || processed == total)
                {
                    await progressCallback(processed * 100 / total, $"{processed}/{total}");
                }
            }
        }

        // 6. 使用 Union-Find 聚类
        var groups = ClusterByUnionFind(pairs.Select(p => (p.r1, p.r2, p.score)).ToList(), resources.Select(r => r.Id));

        // 7. 持久化结果
        await SaveResultsAsync(planId, groups, resources, pairs);

        // 8. 更新 LastRunAt
        await UpdateLastRunAtAsync(planId);
    }

    private async Task<List<Resource>> GetFilteredResourcesAsync(ResourceSearch? search, IResourceService resourceService, CancellationToken ct)
    {
        // 使用 ResourceSearch 过滤资源
        search ??= new ResourceSearch();
        search.PageSize = int.MaxValue;
        var result = await resourceService.Search(search);
        return result.Data?.ToList() ?? [];
    }

    private List<List<int>> ClusterByUnionFind(List<(int r1, int r2, double score)> pairs, IEnumerable<int> allIds)
    {
        var parent = allIds.ToDictionary(id => id, id => id);

        int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
                parent[px] = py;
        }

        foreach (var (r1, r2, _) in pairs)
        {
            Union(r1, r2);
        }

        var groups = parent.Keys
            .GroupBy(Find)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        return groups;
    }

    private async Task SaveResultsAsync(int planId, List<List<int>> groups, List<Resource> resources,
        List<(int r1, int r2, double score, List<RuleScoreDetail> details)> pairs)
    {
        var resourceMap = resources.ToDictionary(r => r.Id);

        // Build a lookup for pairs by resource IDs
        var pairLookup = pairs.ToDictionary(p => (Math.Min(p.r1, p.r2), Math.Max(p.r1, p.r2)));

        foreach (var group in groups)
        {
            var groupDb = new ComparisonResultGroupDbModel
            {
                PlanId = planId,
                MemberCount = group.Count,
                CreatedAt = DateTime.Now
            };
            await _groupOrm.Add(groupDb);

            // 选择推荐保留的资源（基于元数据完整度）
            var primaryId = SelectPrimaryResource(group, resourceMap);

            var members = group.Select(resourceId => new ComparisonResultGroupMemberDbModel
            {
                GroupId = groupDb.Id,
                ResourceId = resourceId,
                IsSuggestedPrimary = resourceId == primaryId
            }).ToList();

            await _memberOrm.AddRange(members);

            // Save pairs for this group
            var groupPairs = new List<ComparisonResultPairDbModel>();
            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    var key = (Math.Min(group[i], group[j]), Math.Max(group[i], group[j]));
                    if (pairLookup.TryGetValue(key, out var pairData))
                    {
                        groupPairs.Add(new ComparisonResultPairDbModel
                        {
                            GroupId = groupDb.Id,
                            Resource1Id = pairData.r1,
                            Resource2Id = pairData.r2,
                            TotalScore = pairData.score,
                            RuleScoresJson = System.Text.Json.JsonSerializer.Serialize(pairData.details),
                            CreatedAt = DateTime.Now
                        });
                    }
                }
            }

            if (groupPairs.Count > 0)
            {
                await _pairOrm.AddRange(groupPairs);
            }
        }
    }

    private int SelectPrimaryResource(List<int> resourceIds, Dictionary<int, Resource> resourceMap)
    {
        // 简单策略：选择属性值最多的资源
        return resourceIds
            .Select(id => resourceMap.TryGetValue(id, out var r) ? r : null)
            .Where(r => r != null)
            .OrderByDescending(r => r!.Properties?.Count ?? 0)
            .Select(r => r!.Id)
            .FirstOrDefault(resourceIds.First());
    }

    private async Task UpdateLastRunAtAsync(int planId)
    {
        var plan = await GetByKey(planId);
        if (plan != null)
        {
            plan.LastRunAt = DateTime.Now;
            await Update(plan);
        }
    }

    #endregion

    #region Results

    public async Task<ComparisonResultSearchResponse> SearchResultGroupsAsync(int planId, ComparisonResultSearchInputModel input)
    {
        var baseQuery = DbContext.ComparisonResultGroups
            .Where(g => g.PlanId == planId && g.MemberCount >= input.MinMemberCount);

        // Count hidden groups
        var hiddenCount = await baseQuery.CountAsync(g => g.IsHidden);

        var query = baseQuery;
        if (!input.IncludeHidden)
        {
            query = query.Where(g => !g.IsHidden);
        }

        query = query
            .OrderByDescending(g => g.MemberCount)
            .ThenByDescending(g => g.Id);

        var total = await query.CountAsync();
        var groups = await query
            .Skip(input.PageIndex * input.PageSize)
            .Take(input.PageSize)
            .ToListAsync();

        var groupIds = groups.Select(g => g.Id).ToList();
        var members = await _memberOrm.GetAll(m => groupIds.Contains(m.GroupId));

        var viewModels = groups.Select(g =>
        {
            var groupMembers = members.Where(m => m.GroupId == g.Id).ToList();
            return g.ToViewModel(groupMembers.Select(m => m.ToViewModel()).ToList());
        }).ToList();

        return new ComparisonResultSearchResponse(viewModels, total, hiddenCount, input.PageIndex, input.PageSize);
    }

    public async Task<ComparisonResultGroupViewModel?> GetResultGroupAsync(int groupId)
    {
        var group = await _groupOrm.GetByKey(groupId);
        if (group == null) return null;

        var members = await _memberOrm.GetAll(m => m.GroupId == groupId);
        return group.ToViewModel(members.Select(m => m.ToViewModel()).ToList());
    }

    public async Task<List<int>> GetResultGroupResourceIdsAsync(int groupId)
    {
        var members = await _memberOrm.GetAll(m => m.GroupId == groupId);
        return members.Select(m => m.ResourceId).ToList();
    }

    public async Task ClearResultsAsync(int planId)
    {
        var groupIds = await DbContext.ComparisonResultGroups
            .Where(g => g.PlanId == planId)
            .Select(g => g.Id)
            .ToListAsync();

        if (groupIds.Count > 0)
        {
            await _pairOrm.RemoveAll(p => groupIds.Contains(p.GroupId));
            await _memberOrm.RemoveAll(m => groupIds.Contains(m.GroupId));
            await _groupOrm.RemoveAll(g => g.PlanId == planId);
        }
    }

    public async Task<ComparisonResultPairsResponse> GetResultGroupPairsAsync(int groupId, int limit = 1000)
    {
        var totalCount = await DbContext.ComparisonResultPairs.CountAsync(p => p.GroupId == groupId);

        var pairs = await DbContext.ComparisonResultPairs
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.TotalScore)
            .Take(limit)
            .ToListAsync();

        var viewModels = pairs.Select(p => p.ToViewModel()).ToList();
        return new ComparisonResultPairsResponse(viewModels, totalCount, limit);
    }

    public async Task HideResultGroupAsync(int groupId, bool hidden)
    {
        var group = await _groupOrm.GetByKey(groupId);
        if (group != null)
        {
            group.IsHidden = hidden;
            await _groupOrm.Update(group);
        }
    }

    #endregion

    #region Similarity Calculation

    public double CalculateSimilarity(Resource r1, Resource r2, ComparisonPlan plan)
    {
        using var context = new ComparisonContext(_specialTextService);
        return CalculateSimilarityInternal(r1, r2, plan, context);
    }

    private double CalculateSimilarityInternal(Resource r1, Resource r2, ComparisonPlan plan, ComparisonContext context)
    {
        double totalScore = 0;
        double maxPossibleScore = 0;

        foreach (var rule in plan.Rules.OrderBy(r => r.Order))
        {
            var v1 = GetPropertyValue(r1, rule);
            var v2 = GetPropertyValue(r2, rule);

            // 1. 双边空值处理
            if (v1 == null && v2 == null)
            {
                switch (rule.BothNullBehavior)
                {
                    case NullValueBehavior.Skip:
                        continue;
                    case NullValueBehavior.Fail:
                        if (rule.IsVeto) return 0.0;
                        maxPossibleScore += rule.Weight;
                        continue;
                    case NullValueBehavior.Pass:
                        totalScore += 1.0 * rule.Weight;
                        maxPossibleScore += rule.Weight;
                        continue;
                }
            }

            // 2. 单边空值处理
            if (v1 == null || v2 == null)
            {
                switch (rule.OneNullBehavior)
                {
                    case NullValueBehavior.Skip:
                        continue;
                    case NullValueBehavior.Fail:
                        if (rule.IsVeto) return 0.0;
                        maxPossibleScore += rule.Weight;
                        continue;
                    case NullValueBehavior.Pass:
                        totalScore += 1.0 * rule.Weight;
                        maxPossibleScore += rule.Weight;
                        continue;
                }
            }

            // 3. 计算规则得分
            if (!_strategies.TryGetValue(rule.Mode, out var strategy))
                continue;

            // 3.1 预处理：文本标准化
            var processedV1 = v1;
            var processedV2 = v2;
            if (rule.Normalize)
            {
                var str1 = v1?.ToString();
                var str2 = v2?.ToString();
                if (!string.IsNullOrEmpty(str1))
                    processedV1 = context.GetNormalizedText(str1);
                if (!string.IsNullOrEmpty(str2))
                    processedV2 = context.GetNormalizedText(str2);
            }

            var ruleScore = strategy.Calculate(processedV1, processedV2, rule.Parameter, context);

            // 4. Veto 检查
            if (rule.IsVeto && ruleScore < rule.VetoThreshold)
            {
                return 0.0;
            }

            // 5. 权重累加
            totalScore += ruleScore * rule.Weight;
            maxPossibleScore += rule.Weight;
        }

        if (maxPossibleScore == 0) return 0;
        return (totalScore / maxPossibleScore) * 100;
    }

    private static object? GetPropertyValue(Resource resource, ComparisonRule rule)
    {
        var (_, bizValue) = resource.GetPropertyValue(rule.PropertyPool, rule.PropertyId, rule.PropertyValueScope);
        return bizValue;
    }

    /// <summary>
    /// Calculates similarity and returns detailed information for each rule
    /// </summary>
    private (double Score, List<RuleScoreDetail> Details) CalculateSimilarityWithDetails(
        Resource r1, Resource r2, ComparisonPlan plan, List<ComparisonRuleDbModel> rulesDb, ComparisonContext context)
    {
        double totalScore = 0;
        double maxPossibleScore = 0;
        var details = new List<RuleScoreDetail>();

        foreach (var rule in plan.Rules.OrderBy(r => r.Order))
        {
            var ruleDb = rulesDb.FirstOrDefault(r => r.Id == rule.Id);
            var v1 = GetPropertyValue(r1, rule);
            var v2 = GetPropertyValue(r2, rule);

            var detail = new RuleScoreDetail
            {
                RuleId = rule.Id,
                Order = rule.Order,
                Weight = rule.Weight,
                Value1 = SerializeValue(v1),
                Value2 = SerializeValue(v2)
            };

            // 1. 双边空值处理
            if (v1 == null && v2 == null)
            {
                switch (rule.BothNullBehavior)
                {
                    case NullValueBehavior.Skip:
                        detail.IsSkipped = true;
                        details.Add(detail);
                        continue;
                    case NullValueBehavior.Fail:
                        if (rule.IsVeto)
                        {
                            detail.IsVetoed = true;
                            detail.Score = 0;
                            details.Add(detail);
                            return (0.0, details);
                        }
                        detail.Score = 0;
                        maxPossibleScore += rule.Weight;
                        details.Add(detail);
                        continue;
                    case NullValueBehavior.Pass:
                        detail.Score = 1.0;
                        totalScore += 1.0 * rule.Weight;
                        maxPossibleScore += rule.Weight;
                        details.Add(detail);
                        continue;
                }
            }

            // 2. 单边空值处理
            if (v1 == null || v2 == null)
            {
                switch (rule.OneNullBehavior)
                {
                    case NullValueBehavior.Skip:
                        detail.IsSkipped = true;
                        details.Add(detail);
                        continue;
                    case NullValueBehavior.Fail:
                        if (rule.IsVeto)
                        {
                            detail.IsVetoed = true;
                            detail.Score = 0;
                            details.Add(detail);
                            return (0.0, details);
                        }
                        detail.Score = 0;
                        maxPossibleScore += rule.Weight;
                        details.Add(detail);
                        continue;
                    case NullValueBehavior.Pass:
                        detail.Score = 1.0;
                        totalScore += 1.0 * rule.Weight;
                        maxPossibleScore += rule.Weight;
                        details.Add(detail);
                        continue;
                }
            }

            // 3. 计算规则得分
            if (!_strategies.TryGetValue(rule.Mode, out var strategy))
            {
                detail.IsSkipped = true;
                details.Add(detail);
                continue;
            }

            // 3.1 预处理：文本标准化
            var processedV1 = v1;
            var processedV2 = v2;
            if (rule.Normalize)
            {
                var str1 = v1?.ToString();
                var str2 = v2?.ToString();
                if (!string.IsNullOrEmpty(str1))
                    processedV1 = context.GetNormalizedText(str1);
                if (!string.IsNullOrEmpty(str2))
                    processedV2 = context.GetNormalizedText(str2);
            }

            var ruleScore = strategy.Calculate(processedV1, processedV2, rule.Parameter, context);
            detail.Score = ruleScore;

            // 4. Veto 检查
            if (rule.IsVeto && ruleScore < rule.VetoThreshold)
            {
                detail.IsVetoed = true;
                details.Add(detail);
                return (0.0, details);
            }

            // 5. 权重累加
            totalScore += ruleScore * rule.Weight;
            maxPossibleScore += rule.Weight;
            details.Add(detail);
        }

        var finalScore = maxPossibleScore == 0 ? 0 : (totalScore / maxPossibleScore) * 100;
        return (finalScore, details);
    }

    private static string? SerializeValue(object? value)
    {
        if (value == null) return null;
        if (value is string str) return str;
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }
        catch
        {
            return value.ToString();
        }
    }

    #endregion
}
