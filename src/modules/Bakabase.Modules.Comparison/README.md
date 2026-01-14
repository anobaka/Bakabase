# 媒体资源对比系统 (Comparison) 执行文档

**版本**: 1.0
**日期**: 2026-01-12
**模块**: `Bakabase.Modules.Comparison`

---

## 1. 概述

基于用户自定义规则的资源对比系统，支持加权评分、一票否决、空值策略等机制，用于识别逻辑重复的媒体资源。

---

## 2. 数据模型设计

### 2.1 枚举定义

```csharp
// Models/Domain/Constants/ComparisonMode.cs
namespace Bakabase.Modules.Comparison.Models.Domain.Constants;

/// <summary>
/// 对比模式
/// </summary>
public enum ComparisonMode
{
    StrictEqual = 0,           // 完全相等
    NormalizedText = 1,        // 调用 SpecialTextService 清洗后对比
    TextSimilarity = 2,        // 编辑距离 / Jaccard 相似度
    RegexExtractNumber = 3,    // 正则提取数字进行整数对比
    FixedTolerance = 4,        // 固定容差 Abs(A-B) <= X
    RelativeTolerance = 5,     // 相对容差 Abs(A-B)/Max <= X%
    SetIntersection = 6,       // 集合交集 Jaccard Index
    Subset = 7,                // 子集判定
    TimeWindow = 8,            // 时间窗口
    SameDay = 9,               // 同一天
    ExtensionMap = 10          // 文件扩展名分布
}
```

```csharp
// Models/Domain/Constants/NullValueBehavior.cs
namespace Bakabase.Modules.Comparison.Models.Domain.Constants;

/// <summary>
/// 空值处理行为（适用于单边空值和双边空值）
/// </summary>
public enum NullValueBehavior
{
    Skip = 0,   // 跳过此规则，不参与计算
    Fail = 1,   // 视为不匹配 (ruleScore = 0)
    Pass = 2    // 视为匹配 (ruleScore = 1)
}
```

### 2.2 ComparisonRule（规则）

**Domain Model:**
```csharp
// Models/Domain/ComparisonRule.cs
namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonRule
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int Order { get; set; }                    // 规则顺序

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyValueScope? PropertyValueScope { get; set; }  // null = 任意来源

    public ComparisonMode Mode { get; set; }
    public string? Parameter { get; set; }            // JSON 序列化的参数

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; } = 1.0;  // 低于此值触发否决

    public NullValueBehavior OneNullBehavior { get; set; } = NullValueBehavior.Skip;
    public NullValueBehavior BothNullBehavior { get; set; } = NullValueBehavior.Skip;
}
```

**Db Model:**
```csharp
// Models/Db/ComparisonRuleDbModel.cs
namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonRuleDbModel
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int Order { get; set; }

    public int PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public int? PropertyValueScope { get; set; }

    public int Mode { get; set; }
    public string? Parameter { get; set; }

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; }

    public int OneNullBehavior { get; set; }
    public int BothNullBehavior { get; set; }
}
```

**ViewModel:**
```csharp
// Models/View/ComparisonRuleViewModel.cs
namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonRuleViewModel
{
    public int Id { get; set; }
    public int Order { get; set; }

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public string? PropertyName { get; set; }         // 展示用
    public PropertyValueScope? PropertyValueScope { get; set; }

    public ComparisonMode Mode { get; set; }
    public object? Parameter { get; set; }            // 反序列化后的参数

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; }

    public NullValueBehavior OneNullBehavior { get; set; }
    public NullValueBehavior BothNullBehavior { get; set; }
}
```

**InputModel:**
```csharp
// Models/Input/ComparisonRuleInputModel.cs
namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonRuleInputModel
{
    public int Order { get; set; }

    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public PropertyValueScope? PropertyValueScope { get; set; }

    public ComparisonMode Mode { get; set; }
    public object? Parameter { get; set; }

    public int Weight { get; set; }
    public bool IsVeto { get; set; }
    public double VetoThreshold { get; set; } = 1.0;

    public NullValueBehavior OneNullBehavior { get; set; } = NullValueBehavior.Skip;
    public NullValueBehavior BothNullBehavior { get; set; } = NullValueBehavior.Skip;
}
```

---

### 2.3 ComparisonPlan（方案）

**Domain Model:**
```csharp
// Models/Domain/ComparisonPlan.cs
namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ResourceFilterId { get; set; }        // 可选：关联预筛选条件
    public double Threshold { get; set; } = 80;       // 判定阈值
    public List<ComparisonRule> Rules { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
}
```

**Db Model:**
```csharp
// Models/Db/ComparisonPlanDbModel.cs
namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonPlanDbModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ResourceFilterId { get; set; }
    public double Threshold { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    // Rules 通过外键关联，不在此处存储
}
```

**ViewModel:**
```csharp
// Models/View/ComparisonPlanViewModel.cs
namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonPlanViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ResourceFilterId { get; set; }
    public string? ResourceFilterName { get; set; }   // 展示用
    public double Threshold { get; set; }
    public List<ComparisonRuleViewModel> Rules { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int? ResultGroupCount { get; set; }        // 可选：结果分组数
}
```

**InputModel:**
```csharp
// Models/Input/ComparisonPlanCreateInputModel.cs
namespace Bakabase.Modules.Comparison.Models.Input;

public record ComparisonPlanCreateInputModel
{
    public string Name { get; set; } = string.Empty;
    public int? ResourceFilterId { get; set; }
    public double Threshold { get; set; } = 80;
    public List<ComparisonRuleInputModel> Rules { get; set; } = [];
}

// Models/Input/ComparisonPlanPatchInputModel.cs
public record ComparisonPlanPatchInputModel
{
    public string? Name { get; set; }
    public int? ResourceFilterId { get; set; }
    public double? Threshold { get; set; }
    public List<ComparisonRuleInputModel>? Rules { get; set; }
}
```

---

### 2.4 ComparisonResultGroup（结果分组）

**Domain Model:**
```csharp
// Models/Domain/ComparisonResultGroup.cs
namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonResultGroup
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }
    public List<ComparisonResultGroupMember> Members { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
```

**Db Model:**
```csharp
// Models/Db/ComparisonResultGroupDbModel.cs
namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonResultGroupDbModel
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }              // 冗余字段，便于排序
    public DateTime CreatedAt { get; set; }
}
```

**ViewModel:**
```csharp
// Models/View/ComparisonResultGroupViewModel.cs
namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonResultGroupViewModel
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int MemberCount { get; set; }
    public List<ComparisonResultGroupMemberViewModel>? Members { get; set; }  // 可选加载
    public List<string>? PreviewCovers { get; set; }  // 缩略图预览（前3个）
    public DateTime CreatedAt { get; set; }
}
```

---

### 2.5 ComparisonResultGroupMember（分组成员）

**Domain Model:**
```csharp
// Models/Domain/ComparisonResultGroupMember.cs
namespace Bakabase.Modules.Comparison.Models.Domain;

public record ComparisonResultGroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }      // 系统推荐保留
}
```

**Db Model:**
```csharp
// Models/Db/ComparisonResultGroupMemberDbModel.cs
namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonResultGroupMemberDbModel
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }
}
```

**ViewModel:**
```csharp
// Models/View/ComparisonResultGroupMemberViewModel.cs
namespace Bakabase.Modules.Comparison.Models.View;

public record ComparisonResultGroupMemberViewModel
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ResourceId { get; set; }
    public bool IsSuggestedPrimary { get; set; }
    // Resource 详情通过单独 API 获取，复用现有 ResourceViewModel
}
```

---

## 3. 项目结构

```
Bakabase/src/modules/Bakabase.Modules.Comparison/
├── Bakabase.Modules.Comparison.csproj
├── ComparisonResource.cs                           # 本地化资源入口
├── Abstractions/
│   ├── Models/
│   │   └── ComparisonContext.cs                    # 对比上下文（含缓存）
│   └── Services/
│       └── IComparisonService.cs
├── Components/
│   ├── IComparisonDbContext.cs                     # DbContext 接口
│   ├── Strategies/                                 # 对比策略实现
│   │   ├── IComparisonStrategy.cs
│   │   ├── StrictEqualStrategy.cs
│   │   ├── NormalizedTextStrategy.cs
│   │   ├── TextSimilarityStrategy.cs
│   │   ├── RegexExtractNumberStrategy.cs
│   │   ├── FixedToleranceStrategy.cs
│   │   ├── RelativeToleranceStrategy.cs
│   │   ├── SetIntersectionStrategy.cs
│   │   ├── SubsetStrategy.cs
│   │   ├── TimeWindowStrategy.cs
│   │   ├── SameDayStrategy.cs
│   │   └── ExtensionMapStrategy.cs
│   └── Tasks/
│       └── ComparisonTask.cs                       # BTask 实现
├── Extensions/
│   └── ServiceCollectionExtensions.cs
├── Models/
│   ├── Domain/
│   │   ├── Constants/
│   │   │   ├── ComparisonMode.cs
│   │   │   └── NullValueBehavior.cs
│   │   ├── ComparisonPlan.cs
│   │   ├── ComparisonRule.cs
│   │   ├── ComparisonResultGroup.cs
│   │   └── ComparisonResultGroupMember.cs
│   ├── Db/
│   │   ├── ComparisonPlanDbModel.cs
│   │   ├── ComparisonRuleDbModel.cs
│   │   ├── ComparisonResultGroupDbModel.cs
│   │   └── ComparisonResultGroupMemberDbModel.cs
│   ├── Input/
│   │   ├── ComparisonPlanCreateInputModel.cs
│   │   ├── ComparisonPlanPatchInputModel.cs
│   │   ├── ComparisonRuleInputModel.cs
│   │   └── ComparisonResultSearchInputModel.cs
│   └── View/
│       ├── ComparisonPlanViewModel.cs
│       ├── ComparisonRuleViewModel.cs
│       ├── ComparisonResultGroupViewModel.cs
│       └── ComparisonResultGroupMemberViewModel.cs
├── Resources/
│   ├── ComparisonResource.resx
│   └── ComparisonResource.zh-Hans.resx
└── Services/
    ├── ComparisonService.cs                        # 核心业务逻辑
    └── ComparisonCacheService.cs                   # 缓存服务
```

---

## 4. 核心算法实现

### 4.1 对比策略接口

```csharp
// Components/Strategies/IComparisonStrategy.cs
namespace Bakabase.Modules.Comparison.Components.Strategies;

public interface IComparisonStrategy
{
    ComparisonMode Mode { get; }

    /// <summary>
    /// 计算两个值的相似度得分
    /// </summary>
    /// <param name="value1">值1</param>
    /// <param name="value2">值2</param>
    /// <param name="parameter">规则参数</param>
    /// <param name="context">对比上下文（含缓存）</param>
    /// <returns>0.0 - 1.0 的得分</returns>
    double Calculate(object? value1, object? value2, object? parameter, ComparisonContext context);
}
```

### 4.2 核心对比逻辑

```csharp
// Services/ComparisonService.cs
namespace Bakabase.Modules.Comparison.Services;

public class ComparisonService : IComparisonService
{
    private readonly ISpecialTextService _specialTextService;
    private readonly Dictionary<ComparisonMode, IComparisonStrategy> _strategies;

    public double CalculateSimilarity(
        Resource r1,
        Resource r2,
        ComparisonPlan plan,
        ComparisonContext context)
    {
        double totalScore = 0;
        double maxPossibleScore = 0;

        foreach (var rule in plan.Rules)
        {
            var v1 = GetPropertyValue(r1, rule, context);
            var v2 = GetPropertyValue(r2, rule, context);

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
            var strategy = _strategies[rule.Mode];
            double ruleScore = strategy.Calculate(v1, v2, rule.Parameter, context);

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

    /// <summary>
    /// 执行全量对比，生成分组结果
    /// </summary>
    public async Task<List<ComparisonResultGroup>> ExecuteComparisonAsync(
        int planId,
        Action<int, string> progressCallback,
        CancellationToken ct)
    {
        // 1. 加载方案
        var plan = await GetPlanAsync(planId);

        // 2. 获取资源列表（应用 ResourceFilter）
        var resources = await GetFilteredResourcesAsync(plan.ResourceFilterId, ct);

        // 3. 初始化缓存上下文
        using var context = new ComparisonContext(_specialTextService);

        // 4. 计算相似度矩阵（N*(N-1)/2 次）
        var pairs = new List<(int r1, int r2, double score)>();
        int total = resources.Count * (resources.Count - 1) / 2;
        int processed = 0;

        for (int i = 0; i < resources.Count; i++)
        {
            for (int j = i + 1; j < resources.Count; j++)
            {
                ct.ThrowIfCancellationRequested();

                var score = CalculateSimilarity(resources[i], resources[j], plan, context);
                if (score >= plan.Threshold)
                {
                    pairs.Add((resources[i].Id, resources[j].Id, score));
                }

                processed++;
                if (processed % 100 == 0)
                {
                    progressCallback(processed * 100 / total, $"{processed}/{total}");
                }
            }
        }

        // 5. 使用 Union-Find 聚类
        var groups = ClusterByUnionFind(pairs, resources.Select(r => r.Id));

        // 6. 持久化结果
        await SaveResultsAsync(planId, groups);

        return groups;
    }
}
```

### 4.3 缓存服务

```csharp
// Services/ComparisonCacheService.cs
namespace Bakabase.Modules.Comparison.Services;

/// <summary>
/// 对比上下文，包含运行时缓存
/// </summary>
public class ComparisonContext : IDisposable
{
    private readonly ISpecialTextService _specialTextService;

    // Key: (ResourceId, PropertyPool, PropertyId, PropertyValueScope?)
    private readonly Dictionary<(int, PropertyPool, int, PropertyValueScope?), string?> _normalizedTextCache = new();
    private readonly Dictionary<(int, PropertyPool, int, PropertyValueScope?), int?> _extractedNumberCache = new();

    public ComparisonContext(ISpecialTextService specialTextService)
    {
        _specialTextService = specialTextService;
    }

    public string? GetNormalizedText(int resourceId, PropertyPool pool, int propertyId,
        PropertyValueScope? scope, string? rawText)
    {
        var key = (resourceId, pool, propertyId, scope);
        if (_normalizedTextCache.TryGetValue(key, out var cached))
            return cached;

        var normalized = rawText != null
            ? _specialTextService.RemoveSpecialContent(rawText)
            : null;
        _normalizedTextCache[key] = normalized;
        return normalized;
    }

    public int? GetExtractedNumber(int resourceId, PropertyPool pool, int propertyId,
        PropertyValueScope? scope, string? rawText, string? regexPattern)
    {
        var key = (resourceId, pool, propertyId, scope);
        if (_extractedNumberCache.TryGetValue(key, out var cached))
            return cached;

        int? number = null;
        if (rawText != null)
        {
            var pattern = regexPattern ?? @"(?i)(?:v|vol|ch|ep|第)\.?\s*(\d+)";
            var match = Regex.Match(rawText, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                number = n;
        }
        _extractedNumberCache[key] = number;
        return number;
    }

    public void Dispose()
    {
        _normalizedTextCache.Clear();
        _extractedNumberCache.Clear();
    }
}
```

---

## 5. BTask 实现

### 5.1 并发策略

| 场景 | 是否允许 | 说明 |
|:---|:---|:---|
| 不同 Plan 并发执行 | **允许** | 对比任务是只读操作，不修改 Resource 和 Property Values |
| 同一 Plan 并发执行 | **禁止** | 避免重复计算和结果覆盖冲突 |

**实现方式**：使用动态 `ConflictKeys`，格式为 `Comparison:{PlanId}`

### 5.2 任务实现

由于需要动态传入 `planId`，不能使用预定义任务（Predefined Task），而是通过 `BTaskManager` 直接创建动态任务。

```csharp
// Services/ComparisonService.cs (任务启动部分)
namespace Bakabase.Modules.Comparison.Services;

public class ComparisonService : IComparisonService
{
    private readonly BTaskManager _taskManager;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 启动对比任务
    /// </summary>
    public async Task<BTask> StartComparisonTaskAsync(int planId)
    {
        var plan = await GetPlanAsync(planId);
        if (plan == null)
            throw new ArgumentException($"Plan {planId} not found");

        var taskId = $"Comparison:{planId}";
        var conflictKey = $"Comparison:{planId}";  // 同一 Plan 不可并发

        var handler = new BTaskHandlerBuilder()
            .WithId(taskId)
            .WithName($"对比任务: {plan.Name}")
            .WithDescription($"正在执行对比方案 [{plan.Name}]")
            .WithConflictKeys([conflictKey])
            .WithIsPersistent(true)
            .WithRunAsync(async args =>
            {
                await ExecuteComparisonAsync(
                    planId,
                    async (percentage, process) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = percentage;
                            t.Process = process;
                        });
                    },
                    args.CancellationToken);
            })
            .Build(_serviceProvider);

        return await _taskManager.EnqueueAsync(handler);
    }

    /// <summary>
    /// 执行全量对比，生成分组结果
    /// </summary>
    public async Task ExecuteComparisonAsync(
        int planId,
        Func<int, string, Task> progressCallback,
        CancellationToken ct)
    {
        // 1. 加载方案
        var plan = await GetPlanAsync(planId);

        // 2. 清除旧结果
        await ClearResultsAsync(planId);

        // 3. 获取资源列表（应用 ResourceFilter）
        var resources = await GetFilteredResourcesAsync(plan.ResourceFilterId, ct);

        // 4. 初始化缓存上下文
        using var context = new ComparisonContext(_specialTextService);

        // 5. 计算相似度矩阵（N*(N-1)/2 次）
        var pairs = new List<(int r1, int r2, double score)>();
        int total = resources.Count * (resources.Count - 1) / 2;
        int processed = 0;

        for (int i = 0; i < resources.Count; i++)
        {
            for (int j = i + 1; j < resources.Count; j++)
            {
                ct.ThrowIfCancellationRequested();

                var score = CalculateSimilarity(resources[i], resources[j], plan, context);
                if (score >= plan.Threshold)
                {
                    pairs.Add((resources[i].Id, resources[j].Id, score));
                }

                processed++;
                if (processed % 100 == 0)
                {
                    await progressCallback(processed * 100 / total, $"{processed}/{total}");
                }
            }
        }

        // 6. 使用 Union-Find 聚类
        var groups = ClusterByUnionFind(pairs, resources.Select(r => r.Id));

        // 7. 持久化结果
        await SaveResultsAsync(planId, groups);

        // 8. 更新 LastRunAt
        await UpdatePlanLastRunAtAsync(planId);
    }
}
```

### 5.3 Controller 调用

```csharp
// Controllers/ComparisonPlanController.cs
[HttpPost("{id}/execute")]
public async Task<IActionResult> Execute(int id)
{
    // 检查是否已有同一 Plan 的任务在运行
    var existingTask = _taskManager.GetTask($"Comparison:{id}");
    if (existingTask?.Status == BTaskStatus.Running)
    {
        return Conflict(new { message = "该方案已有对比任务正在执行" });
    }

    var task = await _comparisonService.StartComparisonTaskAsync(id);
    return Ok(new { taskId = task.Id });
}
```

---

## 6. API 设计

### 6.1 ComparisonPlanController

```
POST   /api/comparison/plan                    # 创建方案
GET    /api/comparison/plan                    # 获取所有方案
GET    /api/comparison/plan/{id}               # 获取单个方案
PATCH  /api/comparison/plan/{id}               # 更新方案
DELETE /api/comparison/plan/{id}               # 删除方案
POST   /api/comparison/plan/{id}/duplicate     # 复制方案
POST   /api/comparison/plan/{id}/execute       # 执行对比（启动 BTask）
DELETE /api/comparison/plan/{id}/results       # 清除对比结果
```

### 6.2 ComparisonResultController

```
GET    /api/comparison/plan/{planId}/results   # 获取结果分组（分页、按成员数倒排）
       Query: pageIndex, pageSize, minMemberCount

GET    /api/comparison/plan/{planId}/results/{groupId}  # 获取分组详情
GET    /api/comparison/plan/{planId}/results/{groupId}/resources  # 获取组内资源列表
```

---

## 7. 前端设计

### 7.1 页面结构

```
/comparison
├── plans/                                     # 方案列表页
│   ├── [planId]/                             # 方案编辑页
│   │   └── results/                          # 结果列表页
│   │       └── [groupId]/                    # 分组详情页
```

### 7.2 结果展示

**分组列表页 (`/comparison/plans/{planId}/results`):**
- 按组内成员数量倒排
- 分页展示
- 筛选器：最小成员数（默认 > 1，即过滤无重复的）
- 每行显示：组 ID、成员数量、缩略图预览

**分组详情页 (`/comparison/plans/{planId}/results/{groupId}`):**
- 使用现有 `Resource` 组件渲染组内资源
- 支持选中、批量操作
- 高亮显示推荐保留的资源

### 7.3 组件复用

```tsx
// 使用现有 Resource 组件
import Resource from '@/components/Resource';

function ComparisonGroupDetail({ groupId }: { groupId: number }) {
  const { resources } = useGroupResources(groupId);

  return (
    <div className="grid grid-cols-4 gap-4">
      {resources.map(r => (
        <Resource
          key={r.id}
          resource={r}
          selected={selectedIds.includes(r.id)}
          onSelected={handleSelect}
        />
      ))}
    </div>
  );
}
```

---

## 8. 执行计划

| 阶段 | 任务 | 预估工作量 |
|:---|:---|:---|
| **P1** | 1. 创建 `Bakabase.Modules.Comparison` 项目<br>2. 定义枚举和数据模型<br>3. 创建 DB 表和迁移<br>4. 实现 Repository 层 | 基础 |
| **P2** | 1. 实现 `IComparisonStrategy` 接口及各策略类<br>2. 实现 `ComparisonContext` 缓存<br>3. 实现 `ComparisonService` 核心逻辑<br>4. 集成 `SpecialTextService` | 核心 |
| **P3** | 1. 实现 `ComparisonTask` (BTask)<br>2. 实现进度上报、取消支持 | 任务 |
| **P4** | 1. 实现 `ComparisonPlanController`<br>2. 实现 `ComparisonResultController`<br>3. 添加 Swagger 文档 | API |
| **P5** | 1. 方案管理页（规则配置 UI）<br>2. 结果列表页（分页、筛选）<br>3. 分组详情页（复用 Resource 组件） | UI |
| **P6** | 1. 添加本地化资源<br>2. 添加预设策略模板展示<br>3. 性能测试与优化 | 完善 |

---

## 9. 注意事项

1. **性能优化**：对于大量资源（>1000），考虑分批处理或并行计算
2. **缓存清理**：每次对比任务结束后，确保清理 `ComparisonContext`
3. **结果一致性**：执行新对比前，应清除该方案的旧结果
4. **推荐算法**：`IsSuggestedPrimary` 可基于"元数据完整度"或"文件大小"计算
5. **并发控制**：
   - 不同 Plan 可并发执行（只读操作）
   - 同一 Plan 禁止并发执行（通过 `ConflictKeys` 实现）
   - 启动任务前应检查是否已有同 Plan 任务在运行
6. **模型层次**：所有核心实体（Plan、Rule、ResultGroup、ResultGroupMember）都需要完整的 Domain/Db/ViewModel/InputModel
