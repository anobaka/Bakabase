# 媒体库重构进度跟踪

## 概览

基于 `REFACTORING_PLAN.md` 进行的媒体库系统重构。

**开始日期**: 2025-12-11
**当前状态**: 阶段 4 进行中

---

## 阶段进度

### 阶段 1: 基础架构（后端） ✅ 已完成

| 任务 | 状态 | 完成日期 | 备注 |
|------|------|----------|------|
| 1.1 创建 PathRuleDbModel | ✅ 完成 | 2025-12-11 | |
| 1.2 创建 PathMark 及相关类型 | ✅ 完成 | 2025-12-11 | 包含 PathMarkType, PathMatchMode, PropertyValueType 等枚举 |
| 1.3 创建 MediaLibraryResourceMappingDbModel | ✅ 完成 | 2025-12-11 | |
| 1.4 创建 ResourceProfileDbModel | ✅ 完成 | 2025-12-11 | |
| 1.5 创建 PathRuleQueueItemDbModel | ✅ 完成 | 2025-12-11 | |
| 1.6 创建 EF Core 迁移 | ✅ 完成 | 2025-12-11 | V220AddPathRuleSystem |
| 1.7 实现 IPathRuleService | ✅ 完成 | 2025-12-11 | |
| 1.8 实现 IResourceProfileService | ✅ 完成 | 2025-12-11 | |
| 1.9 实现 IPathRuleQueueService | ✅ 完成 | 2025-12-11 | |
| 1.10 实现 IMediaLibraryResourceMappingService | ✅ 完成 | 2025-12-11 | |
| 1.11 创建 API Controllers | ✅ 完成 | 2025-12-11 | 4 个新 Controller |

**注意**: PathRuleQueueProcessor 和 PathRuleFileWatcher 将在后续阶段实现（后台任务处理）

### 阶段 2: 数据迁移 ✅ 已完成

| 任务 | 状态 | 完成日期 | 备注 |
|------|------|----------|------|
| 2.1 实现 V220Migrator | ✅ 完成 | 2025-12-11 | 迁移 Resource.MediaLibraryId 到映射表，转换 Template 到 PathRule |
| 2.2 实现 PathRuleQueueProcessor | ✅ 完成 | 2025-12-11 | 后台处理队列项 |
| 2.3 实现 PathRuleFileWatcher | ✅ 完成 | 2025-12-11 | 文件系统监控 |

### 阶段 3: API 层增强 ✅ 已完成

| 任务 | 状态 | 完成日期 | 备注 |
|------|------|----------|------|
| 3.1 PathRuleController | ✅ 完成 | 2025-12-11 | 基础 CRUD + 复制/预览 |
| 3.2 ResourceProfileController | ✅ 完成 | 2025-12-11 | 基础 CRUD + 测试条件 |
| 3.3 MediaLibraryResourceMappingController | ✅ 完成 | 2025-12-11 | |
| 3.4 PathRuleQueueController | ✅ 完成 | 2025-12-11 | |
| 3.5 修改 ResourceController 支持多媒体库 | ✅ 完成 | 2025-12-11 | 添加多媒体库映射 API |
| 3.6 修改 MediaLibraryV2Controller | ✅ 完成 | 2025-12-11 | 添加资源/PathRule 关联 API |

### 阶段 4: 前端开发 🔄 进行中

| 任务 | 状态 | 完成日期 | 备注 |
|------|------|----------|------|
| 4.1 PathRule 管理页面 | ✅ 完成 | 2025-12-12 | 列表、添加、编辑、删除、复制、预览 |
| 4.2 ResourceProfile 管理页面 | ✅ 完成 | 2025-12-12 | 列表、添加、编辑、删除、复制、测试 |
| 4.3 文件管理器增强 | ✅ 完成 | 2025-12-12 | 规则指示器、批量配置面板、上下文菜单集成 |
| 4.4 修改现有页面 | ✅ 完成 | 2025-12-12 | 资源详情多媒体库支持、媒体库页面 PathRule 关联显示 |

### 阶段 5: 集成测试与优化

| 任务 | 状态 | 完成日期 | 备注 |
|------|------|----------|------|
| 5.1 端到端测试 | ⏳ 待开始 | | |
| 5.2 性能测试 | ⏳ 待开始 | | |
| 5.3 迁移测试 | ⏳ 待开始 | | |
| 5.4 文档更新 | ⏳ 待开始 | | |

---

## 详细日志

### 2025-12-12 (续2)

**工作内容**:
- 修改现有页面支持新的多媒体库系统
- 资源详情页面添加媒体库映射管理
- 媒体库页面添加 PathRule 关联显示

**完成的工作**:

1. **MediaLibraryResourceMapping API 添加到 SDK** (手动添加到 Api.ts):
   - BakabaseAbstractionsModelsDomainConstantsMappingSource 枚举 (Rule/Manual)
   - BakabaseAbstractionsModelsDomainMediaLibraryResourceMapping 接口
   - BakabaseServiceControllersEnsureMappingsInput 接口
   - mediaLibraryResourceMapping API 方法集 (getAllMappings, getMappingsByResourceId, addMapping, deleteMapping, ensureMappings, replaceMappings)

2. **MediaLibrary Statistics API 添加到 SDK**:
   - BakabaseServiceControllersMediaLibraryStatistics 接口
   - getMediaLibraryV2PathRules API 方法
   - getMediaLibraryV2Statistics API 方法

3. **MediaLibraryMappings 组件** (新文件):
   - src/web/src/components/Resource/components/DetailDialog/MediaLibraryMappings/index.tsx
   - 显示资源所属的媒体库列表
   - 支持添加资源到媒体库
   - 支持从媒体库中移除资源
   - 显示映射来源 (Rule/Manual)

4. **资源详情对话框集成**:
   - src/web/src/components/Resource/components/DetailDialog/index.tsx
   - 导入 MediaLibraryMappings 组件
   - 在 BasicInfo 下方显示媒体库映射

5. **PathRulesIndicator 组件** (新文件):
   - src/web/src/pages/media-library/components/PathRulesIndicator.tsx
   - 显示与媒体库路径关联的 PathRule
   - 点击可导航到 PathRule 管理页面
   - 延迟加载 (Popover 打开时才加载)

6. **媒体库页面集成**:
   - src/web/src/pages/media-library/index.tsx
   - 在操作列添加 PathRulesIndicator
   - 导入组件并添加 Tooltip

---

### 2025-12-12 (续)

**工作内容**:
- 实现文件管理器增强功能
- 添加路径规则指示器组件
- 添加批量配置面板
- 集成到上下文菜单

**完成的工作**:

1. **PathRule API 添加到 SDK** (手动添加到 Api.ts):
   - BakabaseAbstractionsModelsDomainConstantsPathMarkType 枚举
   - BakabaseAbstractionsModelsDomainConstantsPathMatchMode 枚举
   - BakabaseAbstractionsModelsDomainConstantsPropertyValueType 枚举
   - BakabaseAbstractionsModelsDomainPathMark 接口
   - BakabaseAbstractionsModelsDomainPathRule 接口
   - pathRule API 方法集 (getAllPathRules, getPathRule, addPathRule, etc.)

2. **usePathRules Hook** (新文件):
   - src/web/src/pages/file-processor/hooks/usePathRules.ts
   - 加载所有路径规则
   - 根据路径获取规则 (精确匹配和父路径匹配)
   - 检查路径是否有规则

3. **PathRuleIndicator 组件** (新文件):
   - src/web/src/pages/file-processor/components/PathRuleIndicator.tsx
   - 显示路径规则状态的视觉指示器
   - 支持精确匹配和继承规则的不同样式
   - Tooltip 显示规则详情

4. **PathRuleConfigPanel 组件** (新文件):
   - src/web/src/pages/file-processor/components/PathRuleConfigPanel.tsx
   - 批量配置路径规则的对话框
   - 支持添加/删除标记 (Resource/Property 类型)
   - 支持 Layer 和 Regex 匹配模式
   - 支持复制/粘贴规则配置
   - Property 标记支持 Fixed/Dynamic 值类型

5. **Capability 扩展**:
   - src/web/src/pages/file-processor/RootTreeEntry/models.ts
   - 添加 "configure-path-rule" 能力
   - 快捷键: R

6. **ContextMenu 集成**:
   - src/web/src/pages/file-processor/RootTreeEntry/components/ContextMenu.tsx
   - 添加 "Configure path rules for N path(s)" 菜单项
   - 使用 createPortal 打开 PathRuleConfigPanel

---

### 2025-12-12

**工作内容**:
- 实现前端 PathRule 管理页面
- 实现前端 ResourceProfile 管理页面
- 添加路由配置

**完成的工作**:

1. **PathRule 管理页面** (3 个新文件):
   - src/web/src/pages/path-rule/index.tsx - 主页面，包含列表、搜索、操作按钮
   - src/web/src/pages/path-rule/components/PathRuleModal.tsx - 添加/编辑对话框
   - src/web/src/pages/path-rule/components/PathRulePreviewModal.tsx - 预览匹配路径对话框

2. **ResourceProfile 管理页面** (3 个新文件):
   - src/web/src/pages/resource-profile/index.tsx - 主页面，包含列表、搜索、操作按钮
   - src/web/src/pages/resource-profile/components/ResourceProfileModal.tsx - 添加/编辑对话框
   - src/web/src/pages/resource-profile/components/ResourceProfileTestModal.tsx - 测试搜索条件对话框

3. **路由配置更新**:
   - src/web/src/components/routesMenuConfig.ts
   - 添加 PathRule 页面路由 (/path-rule)
   - 添加 ResourceProfile 页面路由 (/resource-profile)
   - 两个页面都标记为 Beta 功能

4. **功能特性**:
   - PathRule 页面: CRUD 操作、复制规则、预览匹配路径
   - ResourceProfile 页面: CRUD 操作、复制配置、测试搜索条件
   - 使用 HeroUI 组件库 (Table, Modal, Button, Input, Chip 等)
   - 遵循项目现有的 Portal 模式创建对话框

---

### 2025-12-11 (续2)

**工作内容**:
- 实现后台任务处理
- 实现文件系统监控
- 修改 ResourceController 支持多媒体库

**完成的工作**:

1. **PathRuleQueueProcessor** (后台服务):
   - src/legacy/Bakabase.InsideWorld.Business/Components/PathRule/PathRuleQueueProcessor.cs
   - 处理队列中的规则应用任务（Apply、Reevaluate、FileSystemChange）
   - 自动发现和创建资源
   - 使用类型别名解决命名空间冲突

2. **PathRuleFileWatcher** (后台服务):
   - src/legacy/Bakabase.InsideWorld.Business/Components/PathRule/PathRuleFileWatcher.cs
   - 监控 PathRule 覆盖路径的文件系统变更
   - 防抖机制避免重复事件
   - 自动刷新 watcher 列表

3. **ResourceController 增强**:
   - 添加多媒体库映射 API
   - GET /resource/{id}/media-libraries - 获取资源的媒体库映射
   - POST /resource/{id}/media-libraries/{mediaLibraryId} - 添加映射
   - DELETE /resource/{id}/media-libraries/{mediaLibraryId} - 删除映射
   - PUT /resource/{id}/media-libraries - 替换所有映射
   - POST /resource/bulk/media-libraries - 批量添加映射

4. **Input Models**:
   - ResourceMediaLibraryMappingInputModel.cs
   - BulkResourceMediaLibraryMappingInputModel (内联类)

5. **BakabaseStartup.cs 更新**:
   - 注册 PathRuleQueueProcessor
   - 注册 PathRuleFileWatcher

6. **MediaLibraryV2Controller 增强**:
   - GET /media-library-v2/{id}/resources - 获取媒体库的资源映射
   - GET /media-library-v2/{id}/resource-count - 获取资源数量
   - GET /media-library-v2/{id}/path-rules - 获取关联的 PathRule
   - DELETE /media-library-v2/{id}/resources - 移除所有资源映射
   - GET /media-library-v2/{id}/statistics - 获取统计信息

7. **View Models**:
   - MediaLibraryStatistics (统计数据模型)

---

### 2025-12-11 (续)

**工作内容**:
- 实现 V220Migrator 数据迁移器

**完成的工作**:

1. **V220Migrator** (1 个新文件):
   - src/miscellaneous/Bakabase.Migrations/V220/V220Migrator.cs
   - 功能 1: 将 Resource.MediaLibraryId 迁移到 MediaLibraryResourceMapping 表
   - 功能 2: 将 MediaLibraryV2 + MediaLibraryTemplate 转换为 PathRule
   - 自动检测已迁移数据，避免重复迁移
   - 处理模板中的 ResourceFilters 和 Properties 转换为 PathMark

---

### 2025-12-11

**工作内容**:
- 分析项目代码结构
- 创建进度跟踪文档
- 完成阶段 1 基础架构实施

**完成的工作**:

1. **枚举类型** (6 个新文件):
   - PathMarkType.cs - 标记类型（Resource/Property）
   - PathMatchMode.cs - 匹配模式（Layer/Regex）
   - PropertyValueType.cs - 属性值类型（Fixed/Dynamic）
   - MappingSource.cs - 映射来源（Rule/Manual）
   - RuleQueueAction.cs - 队列操作类型
   - RuleQueueStatus.cs - 队列状态

2. **领域模型** (8 个新文件):
   - PathMark.cs - 路径标记
   - ResourceMarkConfig.cs - 资源标记配置
   - PropertyMarkConfig.cs - 属性标记配置
   - PathRule.cs - 路径规则
   - SearchCriteria.cs - 搜索条件
   - ResourceProfile.cs - 资源配置档案
   - MediaLibraryResourceMapping.cs - 媒体库资源映射
   - PathRuleQueueItem.cs - 路径规则队列项

3. **数据库模型** (4 个新文件):
   - PathRuleDbModel.cs
   - MediaLibraryResourceMappingDbModel.cs
   - ResourceProfileDbModel.cs
   - PathRuleQueueItemDbModel.cs

4. **扩展方法** (4 个新文件):
   - PathRuleExtensions.cs
   - MediaLibraryResourceMappingExtensions.cs
   - ResourceProfileExtensions.cs
   - PathRuleQueueItemExtensions.cs

5. **服务接口** (4 个新文件):
   - IPathRuleService.cs
   - IMediaLibraryResourceMappingService.cs
   - IResourceProfileService.cs
   - IPathRuleQueueService.cs

6. **服务实现** (4 个新文件):
   - PathRuleService.cs
   - MediaLibraryResourceMappingService.cs
   - ResourceProfileService.cs
   - PathRuleQueueService.cs

7. **API Controller** (4 个新文件):
   - PathRuleController.cs
   - ResourceProfileController.cs
   - MediaLibraryResourceMappingController.cs
   - PathRuleQueueController.cs

8. **数据库迁移** (1 个新文件):
   - 20251211150129_V220AddPathRuleSystem.cs

9. **修改的文件**:
   - InsideWorldDbContext.cs - 添加新 DbSet 和索引配置
   - MediaLibraryTemplateExtensions.cs - 注册新服务

---

## 文件变更记录

### 新增文件

| 文件路径 | 描述 | 阶段 |
|----------|------|------|
| src/abstractions/.../Constants/PathMarkType.cs | 标记类型枚举 | 1 |
| src/abstractions/.../Constants/PathMatchMode.cs | 匹配模式枚举 | 1 |
| src/abstractions/.../Constants/PropertyValueType.cs | 属性值类型枚举 | 1 |
| src/abstractions/.../Constants/MappingSource.cs | 映射来源枚举 | 1 |
| src/abstractions/.../Constants/RuleQueueAction.cs | 队列操作枚举 | 1 |
| src/abstractions/.../Constants/RuleQueueStatus.cs | 队列状态枚举 | 1 |
| src/abstractions/.../Domain/PathMark.cs | 路径标记领域模型 | 1 |
| src/abstractions/.../Domain/ResourceMarkConfig.cs | 资源标记配置 | 1 |
| src/abstractions/.../Domain/PropertyMarkConfig.cs | 属性标记配置 | 1 |
| src/abstractions/.../Domain/PathRule.cs | 路径规则领域模型 | 1 |
| src/abstractions/.../Domain/SearchCriteria.cs | 搜索条件 | 1 |
| src/abstractions/.../Domain/ResourceProfile.cs | 资源配置档案 | 1 |
| src/abstractions/.../Domain/MediaLibraryResourceMapping.cs | 映射领域模型 | 1 |
| src/abstractions/.../Domain/PathRuleQueueItem.cs | 队列项领域模型 | 1 |
| src/abstractions/.../Db/PathRuleDbModel.cs | 路径规则数据库模型 | 1 |
| src/abstractions/.../Db/MediaLibraryResourceMappingDbModel.cs | 映射数据库模型 | 1 |
| src/abstractions/.../Db/ResourceProfileDbModel.cs | 档案数据库模型 | 1 |
| src/abstractions/.../Db/PathRuleQueueItemDbModel.cs | 队列数据库模型 | 1 |
| src/abstractions/.../Extensions/PathRuleExtensions.cs | 路径规则扩展 | 1 |
| src/abstractions/.../Extensions/MediaLibraryResourceMappingExtensions.cs | 映射扩展 | 1 |
| src/abstractions/.../Extensions/ResourceProfileExtensions.cs | 档案扩展 | 1 |
| src/abstractions/.../Extensions/PathRuleQueueItemExtensions.cs | 队列扩展 | 1 |
| src/abstractions/.../Services/IPathRuleService.cs | 路径规则服务接口 | 1 |
| src/abstractions/.../Services/IMediaLibraryResourceMappingService.cs | 映射服务接口 | 1 |
| src/abstractions/.../Services/IResourceProfileService.cs | 档案服务接口 | 1 |
| src/abstractions/.../Services/IPathRuleQueueService.cs | 队列服务接口 | 1 |
| src/legacy/.../Services/PathRuleService.cs | 路径规则服务实现 | 1 |
| src/legacy/.../Services/MediaLibraryResourceMappingService.cs | 映射服务实现 | 1 |
| src/legacy/.../Services/ResourceProfileService.cs | 档案服务实现 | 1 |
| src/legacy/.../Services/PathRuleQueueService.cs | 队列服务实现 | 1 |
| src/Bakabase.Service/Controllers/PathRuleController.cs | 路径规则 API | 1 |
| src/Bakabase.Service/Controllers/ResourceProfileController.cs | 档案 API | 1 |
| src/Bakabase.Service/Controllers/MediaLibraryResourceMappingController.cs | 映射 API | 1 |
| src/Bakabase.Service/Controllers/PathRuleQueueController.cs | 队列 API | 1 |
| src/legacy/.../Migrations/20251211150129_V220AddPathRuleSystem.cs | 数据库迁移 | 1 |
| src/miscellaneous/Bakabase.Migrations/V220/V220Migrator.cs | 数据迁移器 | 2 |
| src/web/src/pages/path-rule/index.tsx | PathRule 列表页面 | 4 |
| src/web/src/pages/path-rule/components/PathRuleModal.tsx | PathRule 编辑对话框 | 4 |
| src/web/src/pages/path-rule/components/PathRulePreviewModal.tsx | PathRule 预览对话框 | 4 |
| src/web/src/pages/resource-profile/index.tsx | ResourceProfile 列表页面 | 4 |
| src/web/src/pages/resource-profile/components/ResourceProfileModal.tsx | ResourceProfile 编辑对话框 | 4 |
| src/web/src/pages/resource-profile/components/ResourceProfileTestModal.tsx | ResourceProfile 测试对话框 | 4 |
| src/web/src/pages/file-processor/hooks/usePathRules.ts | PathRule Hook | 4 |
| src/web/src/pages/file-processor/components/PathRuleIndicator.tsx | 路径规则指示器组件 | 4 |
| src/web/src/pages/file-processor/components/PathRuleConfigPanel.tsx | 路径规则配置面板 | 4 |
| src/web/src/components/Resource/components/DetailDialog/MediaLibraryMappings/index.tsx | 媒体库映射管理组件 | 4 |
| src/web/src/pages/media-library/components/PathRulesIndicator.tsx | 媒体库 PathRule 关联指示器 | 4 |

### 修改文件

| 文件路径 | 描述 | 阶段 |
|----------|------|------|
| src/legacy/.../InsideWorldDbContext.cs | 添加新 DbSet 和索引 | 1 |
| src/legacy/.../Extensions/MediaLibraryTemplateExtensions.cs | 注册新服务 | 1 |
| src/web/src/components/routesMenuConfig.ts | 添加 PathRule 和 ResourceProfile 路由 | 4 |
| src/web/src/sdk/Api.ts | 手动添加 PathRule/MediaLibraryResourceMapping API 类型和方法 | 4 |
| src/web/src/pages/file-processor/RootTreeEntry/models.ts | 添加 configure-path-rule 能力 | 4 |
| src/web/src/pages/file-processor/RootTreeEntry/components/ContextMenu.tsx | 添加路径规则配置菜单项 | 4 |
| src/web/src/components/Resource/components/DetailDialog/index.tsx | 集成 MediaLibraryMappings 组件 | 4 |
| src/web/src/pages/media-library/index.tsx | 集成 PathRulesIndicator 组件 | 4 |

---

## 注意事项

1. **向后兼容**: 所有废弃字段仅标记 `[Obsolete]`，不删除
2. **增量迁移**: 不删除原有数据，新表与旧表并存
3. **测试优先**: 每个阶段完成后进行充分测试

---

## 回滚计划

如果新功能出现问题：
1. 原有数据保留在原表中
2. 可临时回退到旧代码使用原有数据
3. 确认新系统稳定后，在后续版本清理废弃字段

---

## 下一步工作

1. **阶段 4: 前端开发** ✅ 完成
   - ✅ PathRule 管理页面
   - ✅ ResourceProfile 管理页面
   - ✅ 文件管理器增强
   - ✅ 修改现有页面（资源详情页支持多媒体库、媒体库页面支持 PathRule 关联等）

2. **阶段 5: 集成测试与优化** (下一步)
   - ⏳ 端到端测试 - 测试完整的资源同步流程
   - ⏳ 性能测试 - 大量规则和资源的性能验证
   - ⏳ 迁移测试 - 从旧版本迁移的兼容性测试
   - ⏳ 文档更新 - 更新用户文档和 API 文档
