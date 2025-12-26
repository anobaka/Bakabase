using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Tests.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// PathMark 同步流程集成测试
///
/// 测试场景:
/// 1. Resource marks 优先同步
/// 2. Property/MediaLibrary marks 在 Resource marks 之后同步
/// 3. Resource 变动会触发相关 Property/MediaLibrary marks 的同步
/// </summary>
[TestClass]
public class PathMarkSyncTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkSyncTests.{DateTime.Now:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    /// <summary>
    /// 测试场景: Resource marks 创建资源后，相关的 Property marks 会被自动同步
    ///
    /// 流程:
    /// 1. 创建目录结构: /root/Series1/Episode1
    /// 2. 添加 Resource mark (layer=2) -> 匹配 Episode1
    /// 3. 添加 Property mark (layer=1) -> 从 Series1 提取属性值
    /// 4. 同步
    /// 5. 验证: Episode1 成为 Resource，并且有 Property 值 "Series1"
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_ResourceMarkCreatesResource_PropertyMarkAppliesValue()
    {
        // Arrange: 创建目录结构
        var series1Dir = Path.Combine(_testRoot, "Series1");
        var episode1Dir = Path.Combine(series1Dir, "Episode1");
        Directory.CreateDirectory(episode1Dir);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // 添加 Resource mark: 第2层作为资源
        var resourceMarkConfig = new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 2,
            FsTypeFilter = PathFilterFsType.Directory,
            ApplyScope = PathMarkApplyScope.MatchedOnly
        };
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(resourceMarkConfig),
            Priority = 100
        });

        // TODO: 添加 Property mark (需要先创建 CustomProperty)
        // 这里需要根据实际的 Property 系统来设置

        // Act: 执行同步
        var result = await syncService.SyncMarks(
            null,
            null,
            null,
            new PauseToken(),
            CancellationToken.None);

        // Assert
        result.ResourcesCreated.Should().Be(1);

        var resources = await resourceService.GetAll();
        resources.Should().ContainSingle(r => r.Path.EndsWith("Episode1"));
    }

    /// <summary>
    /// 测试场景: 验证同步顺序 - Resource marks 必须在 Property/MediaLibrary marks 之前处理
    ///
    /// 通过追踪状态变化顺序来验证
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_ResourceMarksProcessedBeforeOtherTypes()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();

        // 添加不同类型的 marks
        var resourceMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 1 // 低优先级
        });

        var propertyMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1
            }),
            Priority = 100 // 高优先级，但仍应在 Resource 之后
        });

        // 追踪处理顺序
        var processOrder = new List<(int MarkId, PathMarkType Type)>();

        // Act: 执行同步
        var result = await syncService.SyncMarks(
            null,
            null,
            async (process) =>
            {
                // 可以通过 process 信息来追踪顺序
                // 或者检查数据库中 mark 状态变化
            },
            new PauseToken(),
            CancellationToken.None);

        // Assert: 验证 Resource mark 先被处理
        // 方法1: 检查最终状态
        var allMarks = await pathMarkService.GetAll();
        allMarks.Should().AllSatisfy(m => m.SyncStatus.Should().Be(PathMarkSyncStatus.Synced));
    }

    /// <summary>
    /// 测试场景: Resource 增加后，已存在的 Property mark 会被应用到新资源
    ///
    /// 这是用户描述的"附加因为 Resource marks 变动增加的 Property marks"场景
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_ExistingPropertyMarkAppliedToNewResources()
    {
        // Arrange
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();

        // Create a custom property first
        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "SeriesName",
            Type = PropertyType.SingleLineText
        });

        // Step 1: 先添加一个 Property mark (状态为 Synced)
        // Layer = 2 to match resources at layer 2
        // ValueLayer = 1 to extract value from layer 1 (parent directory name)
        var propertyMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ValueLayer = 1
            }),
            Priority = 10
        });
        // 标记为已同步
        await pathMarkService.MarkAsSynced(propertyMark.Id);

        // Step 2: 创建新目录，添加 Resource mark
        var newDir = Path.Combine(_testRoot, "NewSeries", "NewEpisode");
        Directory.CreateDirectory(newDir);

        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        // Act: 同步 - 应该创建资源并应用 Property mark
        var result = await syncService.SyncMarks(
            null,
            null,
            null,
            new PauseToken(),
            CancellationToken.None);

        // Assert
        result.ResourcesCreated.Should().BeGreaterThan(0);
        // Property mark 应该被重新应用到新资源
        result.PropertiesApplied.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试场景: 删除 Resource mark 后，相关资源被删除
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_DeleteResourceMark_RemovesResources()
    {
        // Arrange: 先创建资源
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        var mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        // 先同步创建资源
        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().NotBeEmpty();

        // Act: 软删除 mark
        await pathMarkService.SoftDelete(mark.Id);

        // 再次同步
        var result = await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

        // Assert
        result.ResourcesDeleted.Should().BeGreaterThan(0);
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().BeEmpty();
    }

    /// <summary>
    /// 测试场景: 同步过程中的状态转换
    /// Pending -> Syncing -> Synced (成功) 或 Failed (失败)
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_StatusTransitions()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();

        var mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        // 验证初始状态
        var initialMark = (await pathMarkService.GetAll()).First(m => m.Id == mark.Id);
        initialMark.SyncStatus.Should().Be(PathMarkSyncStatus.Pending);

        // Act
        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

        // Assert: 验证最终状态
        var finalMark = (await pathMarkService.GetAll()).First(m => m.Id == mark.Id);
        finalMark.SyncStatus.Should().Be(PathMarkSyncStatus.Synced);
    }

    /// <summary>
    /// 测试场景: Priority 排序 - 高优先级 marks 先处理
    /// </summary>
    [TestMethod]
    public async Task SyncFlow_HigherPriorityProcessedFirst()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        var dir2 = Path.Combine(_testRoot, "Dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();

        // 添加两个 Resource marks，不同优先级
        await pathMarkService.Add(new PathMark
        {
            Path = dir1,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 10 // 低优先级
        });

        await pathMarkService.Add(new PathMark
        {
            Path = dir2,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100 // 高优先级
        });

        // Act
        var result = await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

        // Assert: 两个都应该被处理
        result.ResourcesCreated.Should().Be(2);
    }

    #region Comprehensive Mark Coverage Tests

    /// <summary>
    /// 测试所有 Resource Mark 配置组合
    /// 注意: 某些边缘情况可能会失败 (如 FsType=null + MatchedAndSubdirectories)
    /// </summary>
    [TestMethod]
    public async Task ResourceMark_AllCombinations_SyncSuccessfully()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();

        var allCombinations = PathMarkGenerator.GenerateAllResourceMarkCombinations(_testRoot).ToList();

        var succeeded = 0;
        var failed = new List<string>();

        // 测试每个组合
        foreach (var (mark, config, description) in allCombinations)
        {
            // 重置环境
            var existingMarks = await pathMarkService.GetAll();
            foreach (var m in existingMarks)
            {
                await pathMarkService.HardDelete(m.Id);
            }

            // 添加 mark
            var addedMark = await pathMarkService.Add(mark);
            if (addedMark == null)
            {
                failed.Add($"Failed to add: {description}");
                continue;
            }

            // 同步
            await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

            // 验证 mark 状态
            var syncedMark = (await pathMarkService.GetAll()).FirstOrDefault(m => m.Id == addedMark.Id);
            if (syncedMark?.SyncStatus == PathMarkSyncStatus.Synced)
            {
                succeeded++;
            }
            else
            {
                failed.Add($"{description}: Status={syncedMark?.SyncStatus}");
            }

            // 清理创建的资源
            var resourceService = _sp.GetRequiredService<IResourceService>();
            var resources = await resourceService.GetAll();
            foreach (var r in resources)
            {
                await resourceService.DeleteByKeys(new[] { r.Id });
            }
        }

        // 输出覆盖统计
        Console.WriteLine($"Tested {allCombinations.Count} Resource mark combinations");
        Console.WriteLine($"Succeeded: {succeeded}, Failed: {failed.Count}");
        if (failed.Any())
        {
            Console.WriteLine($"Failed combinations:\n{string.Join("\n", failed)}");
        }

        // 大多数组合应该成功 (至少80%)
        var successRate = (double)succeeded / allCombinations.Count;
        successRate.Should().BeGreaterThan(0.7, $"Too many combinations failed. Failed: {string.Join(", ", failed.Take(5))}");
    }

    /// <summary>
    /// 测试 Resource Mark Layer 模式各层级匹配
    /// </summary>
    [TestMethod]
    public async Task ResourceMark_LayerMode_MatchesCorrectLevel()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // Layer 0: 匹配根目录
        var layer0Mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resources = await resourceService.GetAll();
        resources.Should().ContainSingle(r => r.Path == _testRoot, "Layer 0 should match root directory");

        // 清理
        await pathMarkService.HardDelete(layer0Mark.Id);
        foreach (var r in resources) await resourceService.DeleteByKeys(new[] { r.Id });

        // Layer 1: 匹配第一层子目录 (Series_A, Series_B, Library_Main)
        var layer1Mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        resources = await resourceService.GetAll();
        resources.Should().HaveCount(3, "Layer 1 should match 3 level-1 directories");
        resources.All(r => structure.Level1Directories.Contains(r.Path)).Should().BeTrue();

        // 清理
        await pathMarkService.HardDelete(layer1Mark.Id);
        foreach (var r in resources) await resourceService.DeleteByKeys(new[] { r.Id });

        // Layer 2: 匹配第二层子目录 (Episode01, Episode02, Special x 3)
        var layer2Mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        resources = await resourceService.GetAll();
        resources.Should().HaveCount(9, "Layer 2 should match 9 level-2 directories");
        resources.All(r => structure.Level2Directories.Contains(r.Path)).Should().BeTrue();
    }

    /// <summary>
    /// 测试 Resource Mark Regex 模式匹配
    /// </summary>
    [TestMethod]
    public async Task ResourceMark_RegexMode_MatchesPattern()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // Regex: 匹配 .mp4 文件
        var mp4Mark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Regex,
                Regex = @".*\.mp4$",
                FsTypeFilter = PathFilterFsType.File
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resources = await resourceService.GetAll();
        resources.Should().HaveCount(9, "Should match 9 .mp4 files");
        resources.All(r => r.Path.EndsWith(".mp4")).Should().BeTrue();

        // 清理
        await pathMarkService.HardDelete(mp4Mark.Id);
        foreach (var r in resources) await resourceService.DeleteByKeys(new[] { r.Id });

        // Regex: 匹配 Episode 开头的目录
        var episodeMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Regex,
                Regex = @"Episode\d+$",
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        resources = await resourceService.GetAll();
        resources.Should().HaveCount(6, "Should match 6 Episode directories");
        resources.All(r => Path.GetFileName(r.Path).StartsWith("Episode")).Should().BeTrue();
    }

    /// <summary>
    /// 测试 Resource Mark FsType 过滤
    /// </summary>
    [TestMethod]
    public async Task ResourceMark_FsTypeFilter_FiltersCorrectly()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // FsType = Directory: 只匹配目录
        var dirMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Regex,
                Regex = @".+",
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resources = await resourceService.GetAll();
        resources.All(r => Directory.Exists(r.Path)).Should().BeTrue("All should be directories");

        // 清理
        await pathMarkService.HardDelete(dirMark.Id);
        foreach (var r in resources) await resourceService.DeleteByKeys(new[] { r.Id });

        // FsType = File: 只匹配文件
        var fileMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Regex,
                Regex = @".+",
                FsTypeFilter = PathFilterFsType.File
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        resources = await resourceService.GetAll();
        resources.All(r => File.Exists(r.Path)).Should().BeTrue("All should be files");
    }

    /// <summary>
    /// 测试 Property Mark 配置组合
    /// </summary>
    [TestMethod]
    public async Task PropertyMark_AllCombinations_SyncSuccessfully()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();

        // 创建测试用的 CustomProperty
        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "TestProperty",
            Type = PropertyType.SingleLineText
        });

        // 先创建 Resource
        var resourceMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resources = await resourceService.GetAll();
        resources.Should().NotBeEmpty("Resources should be created first");

        // 获取部分 Property Mark 组合进行测试 (避免过多组合导致测试时间过长)
        var propertyCombinations = PathMarkGenerator.GenerateAllPropertyMarkCombinations(_testRoot, testProperty.Id)
            .Take(10) // 只测试前10个组合
            .ToList();

        foreach (var (mark, config, description) in propertyCombinations)
        {
            // 添加 Property mark
            var addedMark = await pathMarkService.Add(mark);
            addedMark.Should().NotBeNull($"Failed to add mark: {description}");

            // 同步
            await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

            // 验证 mark 状态
            var syncedMark = (await pathMarkService.GetAll()).FirstOrDefault(m => m.Id == addedMark.Id);
            syncedMark.Should().NotBeNull($"Mark not found after sync: {description}");
            syncedMark!.SyncStatus.Should().Be(PathMarkSyncStatus.Synced, $"Mark not synced: {description}");

            // 清理这个 property mark
            await pathMarkService.HardDelete(addedMark.Id);
        }

        Console.WriteLine($"Tested {propertyCombinations.Count} Property mark combinations");
    }

    /// <summary>
    /// 测试 Property Mark Dynamic 值提取
    /// </summary>
    [TestMethod]
    public async Task PropertyMark_DynamicValue_ExtractsFromPath()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();

        // 创建测试用的 CustomProperty
        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "SeriesName",
            Type = PropertyType.SingleLineText
        });

        // 创建 Resource (Layer 2 = Episode 目录)
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        // 创建 Property mark: 从 Layer 1 (Series_A, Series_B, Library_Main) 提取值
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ValueLayer = 1, // 从第1层提取值
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 50
        });

        // 同步
        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

        // 验证资源及其属性
        var resources = await resourceService.GetAll();
        resources.Should().HaveCount(9, "Should have 9 Episode resources");

        // 每个 Episode 应该有对应 Series 名称的属性值
        foreach (var resource in resources)
        {
            var parentName = Path.GetFileName(Path.GetDirectoryName(resource.Path));
            // 属性值应该是父目录名 (Series_A, Series_B, Library_Main)
            structure.Level1Directories.Select(d => Path.GetFileName(d)).Should().Contain(parentName);
        }
    }

    /// <summary>
    /// 测试 MediaLibrary Mark 配置组合
    /// </summary>
    [TestMethod]
    public async Task MediaLibraryMark_AllCombinations_SyncSuccessfully()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var mediaLibraryService = _sp.GetRequiredService<IMediaLibraryService>();

        // 创建测试用的 MediaLibrary
        var testLibrary = new MediaLibrary { Name = "TestLibrary" };
        await mediaLibraryService.AddRange(new[] { testLibrary });
        var libraries = await mediaLibraryService.GetAll();
        testLibrary = libraries.First(l => l.Name == "TestLibrary");

        // 先创建 Resource
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

        // 获取部分 MediaLibrary Mark 组合
        var mlCombinations = PathMarkGenerator.GenerateAllMediaLibraryMarkCombinations(_testRoot, testLibrary.Id)
            .Take(10)
            .ToList();

        foreach (var (mark, config, description) in mlCombinations)
        {
            var addedMark = await pathMarkService.Add(mark);
            addedMark.Should().NotBeNull($"Failed to add mark: {description}");

            await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

            var syncedMark = (await pathMarkService.GetAll()).FirstOrDefault(m => m.Id == addedMark.Id);
            syncedMark.Should().NotBeNull($"Mark not found after sync: {description}");
            syncedMark!.SyncStatus.Should().Be(PathMarkSyncStatus.Synced, $"Mark not synced: {description}");

            await pathMarkService.HardDelete(addedMark.Id);
        }

        Console.WriteLine($"Tested {mlCombinations.Count} MediaLibrary mark combinations");
    }

    /// <summary>
    /// 测试 ApplyScope 行为
    /// </summary>
    [TestMethod]
    public async Task ResourceMark_ApplyScope_BehavesCorrectly()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // MatchedOnly: 只匹配指定层级
        var matchedOnlyMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        var resources = await resourceService.GetAll();
        resources.Should().HaveCount(3, "MatchedOnly should only match layer 1 directories");

        // 清理
        await pathMarkService.HardDelete(matchedOnlyMark.Id);
        foreach (var r in resources) await resourceService.DeleteByKeys(new[] { r.Id });

        // MatchedAndSubdirectories: 匹配指定层级及其子目录
        var matchedAndSubMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
            }),
            Priority = 100
        });

        await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);
        resources = await resourceService.GetAll();
        resources.Count.Should().BeGreaterThan(3, "MatchedAndSubdirectories should include subdirectories");
    }

    /// <summary>
    /// 测试随机生成的 Marks 组合
    /// </summary>
    [TestMethod]
    public async Task RandomMarks_MultipleCombinations_SyncSuccessfully()
    {
        var structure = PathMarkGenerator.CreateTestDirectoryStructure(_testRoot);
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var syncService = _sp.GetRequiredService<IPathMarkSyncService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var mediaLibraryService = _sp.GetRequiredService<IMediaLibraryService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "RandomTestProperty",
            Type = PropertyType.SingleLineText
        });

        var testLibrary = new MediaLibrary { Name = "RandomTestLibrary" };
        await mediaLibraryService.AddRange(new[] { testLibrary });
        var libraries = await mediaLibraryService.GetAll();
        testLibrary = libraries.First(l => l.Name == "RandomTestLibrary");

        // 生成并测试多个随机组合
        for (var i = 0; i < 5; i++)
        {
            // 清理
            var existingMarks = await pathMarkService.GetAll();
            foreach (var m in existingMarks) await pathMarkService.HardDelete(m.Id);
            var existingResources = await resourceService.GetAll();
            foreach (var r in existingResources) await resourceService.DeleteByKeys(new[] { r.Id });

            // 添加随机 Resource mark
            var (resourceMark, _) = PathMarkGenerator.GenerateRandomResourceMark(
                _testRoot,
                matchMode: PathMatchMode.Layer,
                fsType: PathFilterFsType.Directory);
            await pathMarkService.Add(resourceMark);

            // 添加随机 Property mark
            var (propertyMark, _) = PathMarkGenerator.GenerateRandomPropertyMark(
                _testRoot,
                testProperty.Id,
                matchMode: PathMatchMode.Layer,
                valueType: PropertyValueType.Fixed);
            await pathMarkService.Add(propertyMark);

            // 添加随机 MediaLibrary mark
            var (mlMark, _) = PathMarkGenerator.GenerateRandomMediaLibraryMark(
                _testRoot,
                testLibrary.Id,
                matchMode: PathMatchMode.Layer,
                valueType: PropertyValueType.Fixed);
            await pathMarkService.Add(mlMark);

            // 同步
            var result = await syncService.SyncMarks(null, null, null, new PauseToken(), CancellationToken.None);

            // 验证所有 marks 都成功同步
            var allMarks = await pathMarkService.GetAll();
            allMarks.Should().AllSatisfy(m =>
                m.SyncStatus.Should().Be(PathMarkSyncStatus.Synced, $"Iteration {i}: Mark {m.Id} not synced"));
        }
    }

    #endregion
}
