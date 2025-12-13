# Bakabase 媒体库重构方案

## 目录

1. [背景与问题分析](#1-背景与问题分析)
2. [核心概念重新定义](#2-核心概念重新定义)
3. [新数据模型设计](#3-新数据模型设计)
4. [路径规则系统](#4-路径规则系统)
5. [增强器/播放器配置系统](#5-增强器播放器配置系统)
6. [UI 交互流程](#6-ui-交互流程)
7. [后台任务处理](#7-后台任务处理)
8. [数据迁移方案](#8-数据迁移方案)
9. [实施阶段](#9-实施阶段)
10. [风险与注意事项](#10-风险与注意事项)

---

## 1. 背景与问题分析

### 1.1 当前架构的问题

| 问题 | 描述 | 影响 |
|------|------|------|
| 路径强关联 | MediaLibrary 与物理路径强绑定 | 无法处理混合资源（多种资源在同一目录） |
| 配置复杂 | 用户需要为 3000+ 作者目录手动配置 | 用户体验差，配置不现实 |
| 目录顺序固定 | 模板假设固定的目录层级顺序 | 无法适应 `2022/电影/导演A` vs `导演A/2022` |
| 资源创建受限 | 只能通过同步媒体库创建资源 | 不够灵活 |
| 一对一关系 | Resource.MediaLibraryId 单一外键 | 资源无法属于多个媒体库 |

### 1.2 现有架构概览

```
┌─────────────────┐     ┌─────────────────────┐     ┌──────────────┐
│  MediaLibraryV2 │────▶│ MediaLibraryTemplate │     │   Resource   │
│                 │     │  - ResourceFilters   │     │              │
│  - Paths[]      │     │  - Properties        │     │ - Path       │
│  - TemplateId   │     │  - Enhancers         │     │ - MediaLibraryId │
│  - Players      │     │  - PlayableFileLocator│    │ - CategoryId │
└─────────────────┘     └─────────────────────┘     └──────────────┘
```

**关键发现：**
- `Resource.CategoryId = 0` 表示资源属于 V2 媒体库
- `.bakabase.json` 标记文件用于跟踪资源 ID（防止路径变更时数据丢失）
- 同步通过 `BTaskManager` 进行后台处理
- `IwFsWatcher` 已存在但主要用于 UI 更新

---

## 2. 核心概念重新定义

### 2.1 新概念模型

```
┌──────────────────────────────────────────────────────────────────┐
│                        Path Rule System                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │ PathRule    │  │ PathRule    │  │ PathRule    │   ...        │
│  │ /movies/*   │  │ /2022/**    │  │ /directorA  │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                         Resource                                 │
│  - Path                                                          │
│  - MediaLibraries[] (多对多)                                      │
│  - Properties[] (来自多条规则的合并)                               │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                 Search-Based Configurations                      │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ ResourceProfile (名称 + 搜索条件 + 配置)                      │ │
│  │  - Enhancer Settings                                        │ │
│  │  - Playable File Settings                                   │ │
│  │  - Player Settings                                          │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 术语定义

| 术语 | 定义 |
|------|------|
| **PathRule** | 路径规则，绑定到特定根路径，包含多个 PathMark |
| **PathMark** | 路径标记，单一职责配置（资源标识 / 属性） |
| **PathMatchMode** | 路径匹配模式，支持 Layer（按层级）和 Regex（按正则表达式）两种 |
| **Layer** | 路径层级，相对于 PathRule.Path 的目录深度（1=第一级子目录） |
| **ResourceProfile** | 资源配置档案，基于搜索条件配置名称模板/增强器/播放器 |

---

## 3. 新数据模型设计

### 3.1 新增表结构

#### 3.1.1 PathRule（路径规则表）

```csharp
public record PathRuleDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 规则绑定的根路径
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// 标记列表 JSON (List<PathMark>)
    /// </summary>
    public string MarksJson { get; set; }

    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
}
```

#### 3.1.2 PathMark（路径标记结构）

**设计原则：每个 Mark 只配置一种类型，职责单一，减少冲突**

```csharp
/// <summary>
/// 标记类型枚举
/// </summary>
public enum PathMarkType
{
    /// <summary>
    /// 资源标识 - 将路径标记为资源
    /// </summary>
    Resource = 1,

    /// <summary>
    /// 属性配置 - 为资源设置属性值（包括媒体库）
    /// </summary>
    Property = 2
}

/// <summary>
/// 路径标记（单一职责）
/// </summary>
public class PathMark
{
    /// <summary>
    /// 标记类型
    /// </summary>
    public PathMarkType Type { get; set; }

    /// <summary>
    /// 优先级（数字越大优先级越高，用于解决同类型 Mark 冲突）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 配置内容 JSON（根据 Type 解析为对应的 Config 类）
    /// </summary>
    public string ConfigJson { get; set; }
}
```

#### 3.1.3 各类型 Mark 的配置结构

```csharp
/// <summary>
/// 路径匹配模式
/// </summary>
public enum PathMatchMode
{
    /// <summary>
    /// 按层级匹配
    /// </summary>
    Layer = 1,

    /// <summary>
    /// 按正则表达式匹配
    /// </summary>
    Regex = 2
}

// ============================================
// Type = Resource 时的配置
// ============================================
public class ResourceMarkConfig
{
    /// <summary>
    /// 匹配模式
    /// </summary>
    public PathMatchMode MatchMode { get; set; }

    /// <summary>
    /// Layer 模式：作用的路径层级（相对于 PathRule.Path）
    /// 1 = 第一级子目录，2 = 第二级子目录，以此类推
    /// -1 = 所有层级
    /// </summary>
    public int? Layer { get; set; }

    /// <summary>
    /// Regex 模式：匹配路径的正则表达式（相对于 PathRule.Path）
    /// </summary>
    public string? Regex { get; set; }

    /// <summary>
    /// 文件系统类型过滤（可选）
    /// </summary>
    public FsType? FsTypeFilter { get; set; }

    /// <summary>
    /// 扩展名过滤（仅当 FsTypeFilter = File 时有效）
    /// </summary>
    public List<string>? Extensions { get; set; }
}

// ============================================
// Type = Property 时的配置（统一处理固定值和动态值）
// ============================================
public class PropertyMarkConfig
{
    /// <summary>
    /// 匹配模式（决定哪些路径会应用此属性配置）
    /// </summary>
    public PathMatchMode MatchMode { get; set; }

    /// <summary>
    /// Layer 模式：作用的路径层级（相对于 PathRule.Path）
    /// 1 = 第一级子目录，2 = 第二级子目录，以此类推
    /// -1 = 所有层级
    /// </summary>
    public int? Layer { get; set; }

    /// <summary>
    /// Regex 模式：匹配路径的正则表达式（相对于 PathRule.Path）
    /// </summary>
    public string? Regex { get; set; }

    public PropertyPool Pool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// 值类型
    /// </summary>
    public PropertyValueType ValueType { get; set; }

    /// <summary>
    /// Fixed 模式：直接的属性值
    /// Dynamic 模式：null
    /// </summary>
    public object? FixedValue { get; set; }

    /// <summary>
    /// Dynamic 模式：从哪一层获取目录名作为值
    /// </summary>
    public int? ValueLayer { get; set; }

    /// <summary>
    /// Dynamic 模式：正则提取（可选）
    /// </summary>
    public string? ValueRegex { get; set; }
}

public enum PropertyValueType
{
    Fixed = 1,   // 固定值
    Dynamic = 2  // 动态值（使用目录名）
}
```

#### 3.1.4 媒体库配置说明

**媒体库通过 Property 类型配置**，使用特殊的 PropertyPool 和 PropertyId：

```csharp
// 媒体库作为特殊属性处理
// Pool = Reserved, PropertyId = MediaLibrary 的保留 ID

// 固定媒体库配置示例（第三层资源使用固定媒体库）
new PropertyMarkConfig
{
    MatchMode = PathMatchMode.Layer,
    Layer = 3,  // 应用到第三层
    Pool = PropertyPool.Reserved,
    PropertyId = ReservedPropertyId.MediaLibrary,  // 特殊保留 ID
    ValueType = PropertyValueType.Fixed,
    FixedValue = 5  // 媒体库 ID
}

// 动态媒体库配置示例（使用第一层目录名匹配媒体库名称）
new PropertyMarkConfig
{
    MatchMode = PathMatchMode.Layer,
    Layer = 3,  // 应用到第三层资源
    Pool = PropertyPool.Reserved,
    PropertyId = ReservedPropertyId.MediaLibrary,
    ValueType = PropertyValueType.Dynamic,
    ValueLayer = 1  // 从第一层目录名匹配媒体库
}
```

#### 3.1.5 配置示例

**场景**：路径 `/movies` 下配置：
- 第三级是资源
- 所有资源使用第二级目录作为【状态】属性
- 所有资源增加固定属性【标签】= "123"
- 将第一级目录名匹配到同名媒体库

```json
{
  "Path": "/movies",
  "Marks": [
    {
      "Type": "Resource",
      "Priority": 10,
      "ConfigJson": "{\"MatchMode\": \"Layer\", \"Layer\": 3, \"FsTypeFilter\": \"Directory\"}"
    },
    {
      "Type": "Property",
      "Priority": 10,
      "ConfigJson": "{\"MatchMode\": \"Layer\", \"Layer\": 3, \"Pool\": \"Custom\", \"PropertyId\": 1, \"ValueType\": \"Dynamic\", \"ValueLayer\": 2}"
    },
    {
      "Type": "Property",
      "Priority": 10,
      "ConfigJson": "{\"MatchMode\": \"Layer\", \"Layer\": 3, \"Pool\": \"Custom\", \"PropertyId\": 2, \"ValueType\": \"Fixed\", \"FixedValue\": \"123\"}"
    },
    {
      "Type": "Property",
      "Priority": 10,
      "ConfigJson": "{\"MatchMode\": \"Layer\", \"Layer\": 3, \"Pool\": \"Reserved\", \"PropertyId\": -1, \"ValueType\": \"Dynamic\", \"ValueLayer\": 1}"
    }
  ]
}
```

**目录结构示例**：
```
/movies/                          # PathRule.Path
├── 电影/                         # Layer 1 → 媒体库名称来源
│   ├── 已看/                     # Layer 2 → 状态属性值来源
│   │   ├── 电影A/                # Layer 3 → 资源，状态="已看"，标签="123"，媒体库="电影"
│   │   └── 电影B/                # Layer 3 → 资源，状态="已看"，标签="123"，媒体库="电影"
│   └── 未看/
│       └── 电影C/                # Layer 3 → 资源，状态="未看"，标签="123"，媒体库="电影"
├── 动漫/
│   └── 连载中/
│       └── 动漫A/                # Layer 3 → 资源，状态="连载中"，标签="123"，媒体库="动漫"
```

**设计优势**：
1. **单一职责**：每个 Mark 只做一件事，易于理解和维护
2. **灵活组合**：可以自由组合多个 Mark 实现复杂配置
3. **独立优先级**：每个 Mark 有独立的 Priority，冲突处理更精细
4. **统一属性处理**：媒体库、自定义属性、保留属性统一为 Property 类型
5. **灵活匹配**：每个配置支持 Layer 和 Regex 两种匹配模式，适应各种目录结构
6. **关注点分离**：PathRule 负责资源识别和属性配置，ResourceProfile 负责显示（NameTemplate）和增强

#### 3.1.6 MediaLibraryResourceMapping（媒体库-资源映射表）

```csharp
public record MediaLibraryResourceMappingDbModel
{
    public int Id { get; set; }
    public int MediaLibraryId { get; set; }
    public int ResourceId { get; set; }

    /// <summary>
    /// 映射来源（规则自动 / 手动配置）
    /// </summary>
    public MappingSource Source { get; set; }

    /// <summary>
    /// 来源规则 ID（如果是规则自动生成）
    /// </summary>
    public int? SourceRuleId { get; set; }

    public DateTime CreateDt { get; set; }
}

public enum MappingSource
{
    Rule = 1,
    Manual = 2
}
```

#### 3.1.7 ResourceProfile（资源配置档案表）

```csharp
public record ResourceProfileDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 搜索条件 JSON
    /// </summary>
    public string SearchCriteriaJson { get; set; }

    /// <summary>
    /// 名称模板（可选），支持变量如 {Name}, {Layer1}, {Layer2} 等
    /// </summary>
    public string? NameTemplate { get; set; }

    /// <summary>
    /// 增强器配置 JSON
    /// </summary>
    public string? EnhancerSettingsJson { get; set; }

    /// <summary>
    /// 可播放文件配置 JSON
    /// </summary>
    public string? PlayableFileSettingsJson { get; set; }

    /// <summary>
    /// 播放器配置 JSON
    /// </summary>
    public string? PlayerSettingsJson { get; set; }

    /// <summary>
    /// 优先级（多个 Profile 匹配同一资源时使用）
    /// </summary>
    public int Priority { get; set; }

    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
}
```

#### 3.1.8 SearchCriteria（搜索条件结构）

```csharp
public class SearchCriteria
{
    /// <summary>
    /// 媒体库过滤
    /// </summary>
    public List<int>? MediaLibraryIds { get; set; }

    /// <summary>
    /// 属性过滤条件
    /// </summary>
    public List<PropertyFilter>? PropertyFilters { get; set; }

    /// <summary>
    /// 路径模式匹配
    /// </summary>
    public string? PathPattern { get; set; }

    /// <summary>
    /// 标签过滤
    /// </summary>
    public ResourceTag? TagFilter { get; set; }
}

public class PropertyFilter
{
    public PropertyPool Pool { get; set; }
    public int PropertyId { get; set; }
    public FilterOperation Operation { get; set; }
    public object? Value { get; set; }
}

public enum FilterOperation
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    IsEmpty,
    IsNotEmpty
}
```

### 3.2 修改现有表结构

#### 3.2.1 Resource 表变更

```csharp
public record ResourceDbModel
{
    public int Id { get; set; }
    public string Path { get; set; }
    public bool IsFile { get; set; }
    public DateTime CreateDt { get; set; }
    public DateTime UpdateDt { get; set; }
    public DateTime FileCreateDt { get; set; }
    public DateTime FileModifyDt { get; set; }

    [Obsolete("使用 MediaLibraryResourceMapping 表替代")]
    public int MediaLibraryId { get; set; }

    [Obsolete("媒体库不再关联 Category")]
    public int CategoryId { get; set; }

    public int? ParentId { get; set; }
    public ResourceTag Tags { get; set; }
    public DateTime? PlayedAt { get; set; }

    /// <summary>
    /// 资源来源规则 ID（新增）
    /// </summary>
    public int? SourceRuleId { get; set; }
}
```

#### 3.2.2 MediaLibraryV2 表变更

```csharp
public record MediaLibraryV2DbModel
{
    public int Id { get; set; }
    public string Name { get; set; }

    [Obsolete("媒体库不再直接关联路径，改用 PathRule")]
    public string Paths { get; set; }

    [Obsolete("媒体库不再使用模板，改用 PathRule + ResourceProfile")]
    public int? TemplateId { get; set; }

    public int ResourceCount { get; set; }
    public string? Color { get; set; }

    [Obsolete("不再需要同步版本")]
    public string? SyncVersion { get; set; }

    [Obsolete("播放器配置移至 ResourceProfile")]
    public string? Players { get; set; }
}
```

### 3.3 新增索引

```sql
-- PathRule 唯一索引（每个路径只能有一条规则）
CREATE UNIQUE INDEX IX_PathRule_Path ON PathRules(Path);

-- MediaLibraryResourceMapping 索引
CREATE INDEX IX_MLRM_MediaLibraryId ON MediaLibraryResourceMappings(MediaLibraryId);
CREATE INDEX IX_MLRM_ResourceId ON MediaLibraryResourceMappings(ResourceId);
CREATE UNIQUE INDEX IX_MLRM_Unique ON MediaLibraryResourceMappings(MediaLibraryId, ResourceId);

-- ResourceProfile 索引
CREATE INDEX IX_ResourceProfile_Priority ON ResourceProfiles(Priority DESC);
```

---

## 4. 路径规则系统

### 4.1 规则匹配算法

```csharp
public interface IPathRuleService
{
    /// <summary>
    /// 获取路径适用的所有规则（按优先级排序）
    /// </summary>
    Task<List<PathRule>> GetApplicableRules(string path);

    /// <summary>
    /// 合并多条规则的标记（内部规则优先）
    /// </summary>
    PathMark MergeMarks(List<PathRule> rules);

    /// <summary>
    /// 将规则应用到路径，创建/更新资源
    /// </summary>
    Task ApplyRules(string path, PathMark mergedMark);
}
```

### 4.2 规则优先级处理

```
规则优先级（从高到低）：
1. 精确路径匹配 > 通配符匹配
2. 更长路径 > 更短路径（更具体的路径优先）
3. Priority 字段值（数字越大优先级越高）

示例：
路径: /movies/2022/action/movie1

匹配的规则（按优先级）：
1. /movies/2022/action/movie1 (精确匹配, Priority: 100)
2. /movies/2022/action/* (通配符, Priority: 50)
3. /movies/2022/** (递归通配符, Priority: 30)
4. /movies/** (递归通配符, Priority: 10)
```

### 4.3 规则处理队列

```csharp
public record PathRuleQueueItem
{
    public int Id { get; set; }

    /// <summary>
    /// 待处理的路径
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// 处理类型
    /// </summary>
    public RuleQueueAction Action { get; set; }

    /// <summary>
    /// 相关规则 ID
    /// </summary>
    public int? RuleId { get; set; }

    public DateTime CreateDt { get; set; }
    public RuleQueueStatus Status { get; set; }
    public string? Error { get; set; }
}

public enum RuleQueueAction
{
    /// <summary>
    /// 应用规则到路径
    /// </summary>
    Apply = 1,

    /// <summary>
    /// 重新评估路径（规则变更时）
    /// </summary>
    Reevaluate = 2,

    /// <summary>
    /// 文件系统变更触发
    /// </summary>
    FileSystemChange = 3
}

public enum RuleQueueStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
```

### 4.4 父子关系处理

```csharp
public class ResourceParentResolver
{
    /// <summary>
    /// 根据路径自动建立父子关系
    /// </summary>
    public async Task ResolveParentRelationships(List<Resource> resources)
    {
        // 1. 按路径深度排序
        var sorted = resources.OrderBy(r => r.Path.Count(c => c == '/')).ToList();

        // 2. 构建路径字典
        var pathDict = sorted.ToDictionary(r => r.Path, r => r);

        // 3. 为每个资源查找父级
        foreach (var resource in sorted)
        {
            var parentPath = Path.GetDirectoryName(resource.Path);
            while (!string.IsNullOrEmpty(parentPath))
            {
                if (pathDict.TryGetValue(parentPath, out var parent))
                {
                    resource.ParentId = parent.Id;
                    break;
                }
                parentPath = Path.GetDirectoryName(parentPath);
            }
        }
    }
}
```

---

## 5. 增强器/播放器配置系统

### 5.1 ResourceProfile 服务

```csharp
public interface IResourceProfileService
{
    /// <summary>
    /// 获取资源匹配的所有配置档案
    /// </summary>
    Task<List<ResourceProfile>> GetMatchingProfiles(Resource resource);

    /// <summary>
    /// 获取资源的有效名称模板
    /// </summary>
    Task<string?> GetEffectiveNameTemplate(Resource resource);

    /// <summary>
    /// 获取资源的有效增强器配置
    /// </summary>
    Task<EnhancerSettings> GetEffectiveEnhancerSettings(Resource resource);

    /// <summary>
    /// 获取资源的有效播放器配置
    /// </summary>
    Task<PlayerSettings> GetEffectivePlayerSettings(Resource resource);

    /// <summary>
    /// 获取资源的可播放文件配置
    /// </summary>
    Task<PlayableFileSettings> GetEffectivePlayableFileSettings(Resource resource);
}
```

### 5.2 配置变更处理

```csharp
public class ResourceProfileChangeHandler
{
    /// <summary>
    /// 增强器配置变更时
    /// </summary>
    public async Task OnEnhancerSettingsChanged(
        ResourceProfile profile,
        EnhancerSettings oldSettings,
        EnhancerSettings newSettings)
    {
        // 1. 计算影响的资源
        var affectedResources = await GetAffectedResources(profile);

        // 2. 提示用户是否需要重新增强
        var userConfirmed = await PromptUser(
            "增强器配置已变更，是否重新增强相关资源？",
            $"将影响 {affectedResources.Count} 个资源"
        );

        if (userConfirmed)
        {
            // 3. 停止自动增强任务
            await StopAutoEnhanceJob();

            // 4. 删除相关增强数据
            await DeleteEnhancementData(affectedResources, oldSettings.EnhancerIds);

            // 5. 标记资源需要重新增强
            await MarkForReEnhancement(affectedResources);

            // 6. 下次自动增强时会处理
        }
    }

    /// <summary>
    /// 可播放文件配置变更时
    /// </summary>
    public async Task OnPlayableFileSettingsChanged(
        ResourceProfile profile,
        PlayableFileSettings oldSettings,
        PlayableFileSettings newSettings)
    {
        var affectedResources = await GetAffectedResources(profile);

        var userConfirmed = await PromptUser(
            "可播放文件配置已变更，是否重新准备缓存？",
            $"将影响 {affectedResources.Count} 个资源"
        );

        if (userConfirmed)
        {
            await StopCachePreparationJob();
            await DeleteCacheData(affectedResources);
            await MarkForCachePreparation(affectedResources);
        }
    }
}
```

### 5.3 自动任务集成

```csharp
public class AutoEnhanceJob : IBTaskHandler
{
    public async Task Run(BTaskRunContext context)
    {
        // 1. 获取所有 ResourceProfile
        var profiles = await profileService.GetAll();

        // 2. 按优先级排序
        profiles = profiles.OrderByDescending(p => p.Priority).ToList();

        // 3. 对每个 Profile
        foreach (var profile in profiles)
        {
            // 4. 获取匹配的资源
            var resources = await searchService.Search(profile.SearchCriteria);

            // 5. 过滤出需要增强的资源
            var needEnhancement = resources.Where(r =>
                !HasCompletedEnhancement(r, profile.EnhancerSettings)
            ).ToList();

            // 6. 执行增强
            foreach (var resource in needEnhancement)
            {
                await enhancerService.Enhance(resource, profile.EnhancerSettings);
            }
        }
    }
}
```

---

## 6. UI 交互流程

### 6.1 文件管理器配置界面

```
┌─────────────────────────────────────────────────────────────────────┐
│ 文件管理器 - /movies/2022                                    [配置] │
├─────────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │ [☑] action/          │ 规则: 媒体库[电影], 类型=动作   [复制规则] │ │
│ │ [☑] comedy/          │ 规则: 媒体库[电影], 类型=喜剧   [复制规则] │ │
│ │ [☐] drama/           │ (无规则)                                 │ │
│ │ [☐] horror/          │ (无规则)                                 │ │
│ │ ...                  │                                          │ │
│ └─────────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│ 已选择 2 项                                                         │
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │ [批量操作]                                                      │ │
│ │   ☐ 设置媒体库: [选择媒体库 ▼]                                  │ │
│ │   ☐ 固定属性值: [选择属性 ▼] = [输入值]                         │ │
│ │   ☐ 动态属性值: [选择属性 ▼] ← 文件夹名称                       │ │
│ │   ☐ 标记为资源: ○ 当前级别 ○ 下一级别 ○ 两者都有                │ │
│ │                                                                  │ │
│ │ 作用范围: ○ 仅当前路径 ○ 仅子路径 ○ 当前及所有子路径            │ │
│ │                                                                  │ │
│ │                     [粘贴规则] [取消] [应用规则]                │ │
│ └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### 6.2 规则复制/应用功能

**设计理念**：不使用 Template 引用机制，而是通过复制/粘贴实现配置复用。

**为什么不使用 Template**：
- Template 变更时会影响所有引用它的路径，可能造成用户未预期的破坏性影响
- 用户可能不记得哪些路径引用了某个 Template
- 每个 PathRule 的配置独立，修改一个不会影响其他

**复制/粘贴工作流程**：

```
1. 用户在已配置规则的路径上点击 [复制规则]
   → 将该路径的 Marks 配置保存到剪贴板/临时存储

2. 用户选择一个或多个目标路径

3. 用户点击 [粘贴规则]
   → 为每个选中的路径创建独立的 PathRule
   → 每个 PathRule 的 MarksJson 是复制内容的副本
   → 后续修改任一路径的规则不会影响其他路径
```

**API 设计**：

```csharp
public interface IPathRuleService
{
    /// <summary>
    /// 复制规则配置（返回 Marks 的 JSON）
    /// </summary>
    Task<string> CopyRuleConfig(string sourcePath);

    /// <summary>
    /// 将规则配置应用到多个目标路径
    /// </summary>
    Task ApplyRuleConfig(string marksJson, List<string> targetPaths);
}
```

**优势**：
1. **配置独立**：每个路径的规则互不影响
2. **用户可控**：用户明确知道配置被应用到了哪些路径
3. **简单直观**：类似于文件的复制/粘贴操作，用户容易理解
4. **无隐式依赖**：没有 Template 引用关系需要管理

### 6.3 资源配置档案界面

```
┌─────────────────────────────────────────────────────────────────────┐
│ 资源配置档案                                           [+ 新建档案] │
├─────────────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │ 电影增强配置                                          [编辑] ✕ │ │
│ │ ├─ 搜索条件: 媒体库 = [电影]                                   │ │
│ │ ├─ 名称模板: {Name}                                            │ │
│ │ ├─ 增强器: TMDB, Regex                                         │ │
│ │ ├─ 可播放文件: .mp4, .mkv, .avi                                │ │
│ │ └─ 播放器: PotPlayer                                           │ │
│ ├─────────────────────────────────────────────────────────────────┤ │
│ │ 动漫增强配置                                          [编辑] ✕ │ │
│ │ ├─ 搜索条件: 媒体库 = [动漫], 年份 >= 2020                     │ │
│ │ ├─ 名称模板: {Layer1} - {Name}                                 │ │
│ │ ├─ 增强器: Bangumi, Regex                                      │ │
│ │ ├─ 可播放文件: .mp4, .mkv                                      │ │
│ │ └─ 播放器: 系统默认                                            │ │
│ └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### 6.3 前端组件结构

```
src/ClientApp/src/pages/
├── FileExplorer/
│   ├── index.tsx                    # 文件管理器主页面
│   ├── components/
│   │   ├── FileList.tsx             # 文件列表
│   │   ├── RuleIndicator.tsx        # 规则指示器（显示在每项右侧）
│   │   ├── BatchConfigPanel.tsx     # 批量配置面板
│   │   └── RuleEditor.tsx           # 规则编辑器
│   └── hooks/
│       ├── usePathRules.ts          # 路径规则 Hook
│       └── useResourceCreation.ts   # 资源创建 Hook
│
├── ResourceProfiles/
│   ├── index.tsx                    # 配置档案列表页
│   ├── ProfileEditor.tsx            # 档案编辑器
│   └── components/
│       ├── SearchCriteriaBuilder.tsx # 搜索条件构建器
│       ├── NameTemplateConfig.tsx    # 名称模板配置
│       ├── EnhancerConfig.tsx        # 增强器配置
│       ├── PlayableFileConfig.tsx    # 可播放文件配置
│       └── PlayerConfig.tsx          # 播放器配置
```

---

## 7. 后台任务处理

### 7.1 规则处理队列服务

```csharp
public class PathRuleQueueProcessor : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BTaskManager _taskManager;
    private Timer _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 每 5 秒检查一次队列
        _timer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    private async void ProcessQueue(object state)
    {
        using var scope = _serviceProvider.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IPathRuleQueueService>();

        // 获取待处理项（批量处理）
        var items = await queueService.GetPendingItems(batchSize: 100);

        if (items.Any())
        {
            await _taskManager.Enqueue(new BTaskHandlerBuilder
            {
                Id = $"ProcessPathRules_{DateTime.UtcNow.Ticks}",
                ConflictKeys = new[] { "PathRuleProcessing" },
                Run = async ctx => await ProcessItems(items, ctx)
            });
        }
    }

    private async Task ProcessItems(List<PathRuleQueueItem> items, BTaskRunContext ctx)
    {
        foreach (var item in items)
        {
            try
            {
                switch (item.Action)
                {
                    case RuleQueueAction.Apply:
                        await ApplyRuleToPath(item);
                        break;
                    case RuleQueueAction.Reevaluate:
                        await ReevaluatePath(item);
                        break;
                    case RuleQueueAction.FileSystemChange:
                        await HandleFileSystemChange(item);
                        break;
                }
            }
            catch (Exception ex)
            {
                await MarkItemFailed(item, ex.Message);
            }
        }
    }
}
```

### 7.2 文件监视服务

```csharp
public class PathRuleFileWatcher : IHostedService
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly IPathRuleService _pathRuleService;
    private readonly IPathRuleQueueService _queueService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 获取所有配置了 "下一级路径配置" 的规则
        var rules = await _pathRuleService.GetRulesWithChildScope();

        foreach (var rule in rules)
        {
            StartWatching(rule.Path);
        }
    }

    private void StartWatching(string path)
    {
        if (_watchers.ContainsKey(path)) return;

        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            IncludeSubdirectories = false
        };

        watcher.Created += OnFileCreated;
        watcher.Deleted += OnFileDeleted;
        watcher.Renamed += OnFileRenamed;
        watcher.EnableRaisingEvents = true;

        _watchers[path] = watcher;
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // 添加到处理队列
        await _queueService.Enqueue(new PathRuleQueueItem
        {
            Path = e.FullPath,
            Action = RuleQueueAction.FileSystemChange,
            CreateDt = DateTime.UtcNow
        });
    }
}
```

### 7.3 手动同步服务

```csharp
public interface IManualSyncService
{
    /// <summary>
    /// 手动触发规则同步
    /// </summary>
    Task<BTaskResult> TriggerSync(ManualSyncOptions options);
}

public class ManualSyncOptions
{
    /// <summary>
    /// 同步的根路径（可选，为空则同步所有）
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// 指定的规则 ID 列表（可选）
    /// </summary>
    public List<int>? RuleIds { get; set; }

    /// <summary>
    /// 是否强制重新评估已存在的资源
    /// </summary>
    public bool ForceReevaluate { get; set; }
}

public class ManualSyncService : IManualSyncService
{
    public async Task<BTaskResult> TriggerSync(ManualSyncOptions options)
    {
        return await _taskManager.Enqueue(new BTaskHandlerBuilder
        {
            Id = $"ManualSync_{DateTime.UtcNow.Ticks}",
            GetName = () => "手动同步规则",
            ConflictKeys = new[] { "PathRuleProcessing", "ManualSync" },
            Run = async ctx =>
            {
                // 1. 获取要处理的规则
                var rules = await GetRulesToProcess(options);

                // 2. 扫描路径
                foreach (var rule in rules)
                {
                    var paths = await ScanPaths(rule);

                    // 3. 添加到队列
                    foreach (var path in paths)
                    {
                        await _queueService.Enqueue(new PathRuleQueueItem
                        {
                            Path = path,
                            Action = options.ForceReevaluate
                                ? RuleQueueAction.Reevaluate
                                : RuleQueueAction.Apply,
                            RuleId = rule.Id
                        });
                    }
                }

                // 4. 等待队列处理完成
                await WaitForQueueCompletion();
            }
        });
    }
}
```

---

## 8. 数据迁移方案

### 8.1 迁移器实现

```csharp
public class V220Migrator : AbstractMigrator
{
    public override Version ApplyOnVersionEqualsOrBefore => new Version(2, 2, 0);

    protected override async Task MigrateAfterDbMigrationInternal(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<InsideWorldDbContext>();

        // 1. 迁移 MediaLibrary + Template 到 PathRule 和 ResourceProfile
        await MigrateMediaLibrariesToPathRulesAndProfiles(db);

        // 2. 迁移 Resource.MediaLibraryId/CategoryId 到映射表
        await MigrateResourceMappings(db);

        // 3. 迁移 Category.EnhancerOptions 到 ResourceProfile
        await MigrateEnhancerOptionsToProfiles(db);

        // 4. 迁移播放器配置到 ResourceProfile
        await MigratePlayerSettings(db);
    }

    private async Task MigrateMediaLibrariesToPathRulesAndProfiles(InsideWorldDbContext db)
    {
        // V2 媒体库迁移
        var v2Libraries = await db.MediaLibrariesV2
            .Include(m => m.Template)
            .ToListAsync();

        foreach (var library in v2Libraries)
        {
            if (library.Template == null) continue;

            var paths = JsonSerializer.Deserialize<List<string>>(library.Paths);

            foreach (var path in paths)
            {
                // 转换模板到 Marks
                var marksJson = ConvertTemplateToMarks(library, library.Template);

                // 创建路径规则
                var rule = new PathRuleDbModel
                {
                    Path = path,
                    MarksJson = marksJson,
                    CreateDt = DateTime.UtcNow,
                    UpdateDt = DateTime.UtcNow
                };

                db.PathRules.Add(rule);
            }

            // 为每个媒体库创建 ResourceProfile（包含 NameTemplate）
            if (!string.IsNullOrEmpty(library.Template.DisplayNameTemplate))
            {
                var profile = new ResourceProfileDbModel
                {
                    Name = $"{library.Name} - 名称模板",
                    SearchCriteriaJson = JsonSerializer.Serialize(new SearchCriteria
                    {
                        MediaLibraryIds = new List<int> { library.Id }
                    }),
                    NameTemplate = library.Template.DisplayNameTemplate,
                    Priority = 10,
                    CreateDt = DateTime.UtcNow,
                    UpdateDt = DateTime.UtcNow
                };

                db.ResourceProfiles.Add(profile);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task MigrateResourceMappings(InsideWorldDbContext db)
    {
        // 获取所有资源
        var resources = await db.Resources.ToListAsync();

        foreach (var resource in resources)
        {
            // V2 资源 (CategoryId = 0)
            if (resource.CategoryId == 0)
            {
                db.MediaLibraryResourceMappings.Add(new MediaLibraryResourceMappingDbModel
                {
                    MediaLibraryId = resource.MediaLibraryId,
                    ResourceId = resource.Id,
                    Source = MappingSource.Rule,
                    CreateDt = resource.CreateDt
                });
            }
            // V1 资源 - 通过 MediaLibrary 找到对应的媒体库
            else
            {
                var v1Library = await db.MediaLibraries
                    .FirstOrDefaultAsync(m => m.Id == resource.MediaLibraryId);

                if (v1Library != null)
                {
                    // 查找或创建对应的 V2 媒体库
                    var v2Library = await FindOrCreateV2Library(db, v1Library);

                    db.MediaLibraryResourceMappings.Add(new MediaLibraryResourceMappingDbModel
                    {
                        MediaLibraryId = v2Library.Id,
                        ResourceId = resource.Id,
                        Source = MappingSource.Rule,
                        CreateDt = resource.CreateDt
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private string ConvertTemplateToMarks(
        MediaLibraryV2DbModel library,
        MediaLibraryTemplateDbModel template)
    {
        var marks = new List<PathMark>();

        // 根据 ResourceFilters 确定资源层级
        var resourceLayer = template.ResourceFilters?.FirstOrDefault()?.Layer ?? 1;

        // 1. 转换 ResourceFilters 到 Resource Mark
        if (template.ResourceFilters?.Any() == true)
        {
            var filter = template.ResourceFilters.First();
            marks.Add(new PathMark
            {
                Type = PathMarkType.Resource,
                Priority = 10,
                ConfigJson = JsonSerializer.Serialize(new ResourceMarkConfig
                {
                    MatchMode = PathMatchMode.Layer,
                    Layer = resourceLayer,
                    FsTypeFilter = filter.FsType,
                    Extensions = filter.Extensions
                })
            });
        }

        // 2. 添加媒体库配置 (Property Mark)
        marks.Add(new PathMark
        {
            Type = PathMarkType.Property,
            Priority = 10,
            ConfigJson = JsonSerializer.Serialize(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = resourceLayer,
                Pool = PropertyPool.Reserved,
                PropertyId = ReservedPropertyId.MediaLibrary,
                ValueType = PropertyValueType.Fixed,
                FixedValue = library.Id
            })
        });

        // 3. 转换属性提取规则 (Property Marks)
        if (template.Properties?.Any() == true)
        {
            foreach (var prop in template.Properties)
            {
                var locator = prop.ValueLocators?.FirstOrDefault();
                marks.Add(new PathMark
                {
                    Type = PathMarkType.Property,
                    Priority = 10,
                    ConfigJson = JsonSerializer.Serialize(new PropertyMarkConfig
                    {
                        MatchMode = PathMatchMode.Layer,
                        Layer = resourceLayer,
                        Pool = prop.Pool,
                        PropertyId = prop.Id,
                        ValueType = PropertyValueType.Dynamic,
                        ValueLayer = locator?.SegmentIndex,
                        ValueRegex = locator?.Regex
                    })
                });
            }
        }

        return JsonSerializer.Serialize(marks);
    }
}
```

### 8.2 迁移说明

**无需回滚机制**：本次迁移采用增量式设计，不删除或覆盖任何现有数据：

1. **新增表**：`PathRules`、`MediaLibraryResourceMappings`、`ResourceProfiles`、`PathRuleQueueItems`
2. **原有字段**：`Resource.MediaLibraryId`、`Resource.CategoryId` 等仅标记为 `[Obsolete]`，不删除
3. **数据复制**：原有数据复制到新表，原表数据保留

这意味着：
- 如果新功能出现问题，可以临时回退到旧代码使用原有数据
- 在确认新系统稳定运行后，可在后续版本中清理废弃字段

---

## 9. 实施阶段

### 阶段 1: 基础架构（后端）

**目标**: 建立新的数据模型和核心服务

**任务**:
1. 创建新的数据库模型
   - `PathRuleDbModel`
   - `MediaLibraryResourceMappingDbModel`
   - `ResourceProfileDbModel`
   - `PathRuleQueueItemDbModel`

2. 创建 EF 迁移
   - 新增表
   - 新增索引
   - 标记废弃字段

3. 实现核心服务
   - `IPathRuleService`
   - `IResourceProfileService`
   - `IPathRuleQueueService`

4. 实现后台任务
   - `PathRuleQueueProcessor`
   - `PathRuleFileWatcher`

### 阶段 2: 数据迁移

**目标**: 平滑迁移现有数据

**任务**:
1. 实现 `V220Migrator`
2. 编写迁移测试
3. 编写数据校验工具

### 阶段 3: API 层

**目标**: 提供新功能的 API 接口

**任务**:
1. `PathRuleController`
   - CRUD 操作
   - 批量操作
   - 规则预览

2. `ResourceProfileController`
   - CRUD 操作
   - 搜索条件测试

3. `ManualSyncController`
   - 触发同步
   - 同步状态查询

4. 修改现有 API
   - `ResourceController` 支持多媒体库
   - `MediaLibraryV2Controller` 移除废弃功能

### 阶段 4: 前端开发

**目标**: 实现新的 UI 交互

**任务**:
1. 文件管理器增强
   - 规则指示器组件
   - 批量配置面板
   - 规则编辑器

2. 资源配置档案页面
   - 列表页
   - 编辑器
   - 搜索条件构建器

3. 修改现有页面
   - 资源页面支持多媒体库显示
   - 媒体库页面移除废弃功能

### 阶段 5: 集成测试与优化

**目标**: 确保系统稳定性

**任务**:
1. 端到端测试
2. 性能测试
3. 迁移测试（使用真实数据备份）
4. 文档更新

---

## 10. 风险与注意事项

### 10.1 风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 数据迁移失败 | 高 | 增量式迁移不删除原数据，迁移前自动备份 |
| 性能下降 | 中 | 添加适当的索引，实现缓存机制 |
| 向后兼容性 | 中 | 保留废弃字段一段时间，可临时回退旧代码 |
| 规则冲突 | 中 | 实现清晰的优先级规则，提供冲突检测 |

### 10.2 兼容性考虑

```csharp
public class ResourceService
{
    /// <summary>
    /// 兼容性方法：获取资源的媒体库
    /// </summary>
    public async Task<List<MediaLibraryV2>> GetMediaLibraries(int resourceId)
    {
        // 优先使用新的映射表
        var mappings = await _mappingService.GetByResourceId(resourceId);

        if (mappings.Any())
        {
            return await _libraryService.GetByIds(mappings.Select(m => m.MediaLibraryId));
        }

        // 回退到旧的方式（过渡期）
        var resource = await _resourceService.GetById(resourceId);
        if (resource.CategoryId == 0)
        {
            var library = await _libraryService.GetById(resource.MediaLibraryId);
            return library != null ? new List<MediaLibraryV2> { library } : new();
        }

        return new List<MediaLibraryV2>();
    }
}
```

### 10.3 ResourceMarker 兼容

```csharp
public class ResourceMarkerService
{
    /// <summary>
    /// 在新架构下继续支持 ResourceMarker
    /// </summary>
    public async Task UpdateMarker(Resource resource)
    {
        var markerPath = Path.Combine(resource.Path, ".bakabase.json");

        // 获取资源关联的所有媒体库
        var mappings = await _mappingService.GetByResourceId(resource.Id);

        var marker = new ResourceMarker
        {
            Ids = new List<int> { resource.Id },
            MediaLibraryIds = mappings.Select(m => m.MediaLibraryId).ToList()
        };

        await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(marker));
    }

    /// <summary>
    /// 通过 Marker 恢复资源关联
    /// </summary>
    public async Task<Resource?> FindByMarker(string directoryPath)
    {
        var markerPath = Path.Combine(directoryPath, ".bakabase.json");

        if (!File.Exists(markerPath)) return null;

        var content = await File.ReadAllTextAsync(markerPath);
        var marker = JsonSerializer.Deserialize<ResourceMarker>(content);

        if (marker?.Ids?.Any() != true) return null;

        // 尝试通过 ID 找到资源
        var resource = await _resourceService.GetById(marker.Ids.First());

        if (resource != null)
        {
            // 更新路径（如果变更了）
            if (resource.Path != directoryPath)
            {
                resource.Path = directoryPath;
                await _resourceService.Update(resource);
            }

            // 恢复媒体库关联（如果需要）
            if (marker.MediaLibraryIds?.Any() == true)
            {
                await _mappingService.EnsureMappings(resource.Id, marker.MediaLibraryIds);
            }
        }

        return resource;
    }
}
```

### 10.4 API 向后兼容

```csharp
[ApiController]
[Route("api/resource")]
public class ResourceController : ControllerBase
{
    /// <summary>
    /// 保持旧 API 兼容
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ResourceDto> GetById(int id)
    {
        var resource = await _resourceService.GetById(id);
        var dto = _mapper.Map<ResourceDto>(resource);

        // 兼容旧的单一媒体库字段
        var mappings = await _mappingService.GetByResourceId(id);
        dto.MediaLibraryId = mappings.FirstOrDefault()?.MediaLibraryId ?? 0;

        // 新增多媒体库字段
        dto.MediaLibraryIds = mappings.Select(m => m.MediaLibraryId).ToList();

        return dto;
    }
}
```

---

## 附录

### A. 完整的类型定义

详见各章节的代码示例。

### B. 数据库变更脚本

```sql
-- 新增 PathRules 表
CREATE TABLE PathRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL,
    MarksJson TEXT NOT NULL,
    CreateDt TEXT NOT NULL,
    UpdateDt TEXT NOT NULL
);

-- 新增 MediaLibraryResourceMappings 表
CREATE TABLE MediaLibraryResourceMappings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MediaLibraryId INTEGER NOT NULL,
    ResourceId INTEGER NOT NULL,
    Source INTEGER NOT NULL,
    SourceRuleId INTEGER,
    CreateDt TEXT NOT NULL,
    FOREIGN KEY (MediaLibraryId) REFERENCES MediaLibrariesV2(Id),
    FOREIGN KEY (ResourceId) REFERENCES Resources(Id)
);

-- 新增 ResourceProfiles 表
CREATE TABLE ResourceProfiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    SearchCriteriaJson TEXT NOT NULL,
    NameTemplate TEXT,
    EnhancerSettingsJson TEXT,
    PlayableFileSettingsJson TEXT,
    PlayerSettingsJson TEXT,
    Priority INTEGER NOT NULL DEFAULT 10,
    CreateDt TEXT NOT NULL,
    UpdateDt TEXT NOT NULL
);

-- 新增 PathRuleQueueItems 表
CREATE TABLE PathRuleQueueItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL,
    Action INTEGER NOT NULL,
    RuleId INTEGER,
    CreateDt TEXT NOT NULL,
    Status INTEGER NOT NULL DEFAULT 0,
    Error TEXT
);

-- 新增索引
CREATE UNIQUE INDEX IX_PathRule_Path ON PathRules(Path);  -- 唯一索引，每个路径只能有一条规则
CREATE INDEX IX_MLRM_MediaLibraryId ON MediaLibraryResourceMappings(MediaLibraryId);
CREATE INDEX IX_MLRM_ResourceId ON MediaLibraryResourceMappings(ResourceId);
CREATE UNIQUE INDEX IX_MLRM_Unique ON MediaLibraryResourceMappings(MediaLibraryId, ResourceId);
CREATE INDEX IX_ResourceProfile_Priority ON ResourceProfiles(Priority DESC);
CREATE INDEX IX_PathRuleQueue_Status ON PathRuleQueueItems(Status);
```

### C. 参考文档

- 现有 MediaLibraryTemplate 结构: `src/abstractions/Bakabase.Abstractions/Models/Db/MediaLibraryTemplateDbModel.cs`
- 现有同步逻辑: `src/legacy/Bakabase.InsideWorld.Business/Services/MediaLibraryV2Service.cs`
- BTaskManager: `src/abstractions/Bakabase.Abstractions/Components/Tasks/BTaskManager.cs`
- ResourceMarker: 搜索 `.bakabase.json` 相关代码
