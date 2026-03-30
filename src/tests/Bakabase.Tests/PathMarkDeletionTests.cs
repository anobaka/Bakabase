using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Tests.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// PathMark 删除功能集成测试
///
/// 测试场景:
/// 1. 不清除标记产生的影响 (cleanupEffects=false)
/// 2. 清除标记产生的影响 (cleanupEffects=true)，包含共享保护逻辑
/// </summary>
[TestClass]
public class PathMarkDeletionTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkDeletionTests.{DateTime.Now:yyyyMMddHHmmssfff}");
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

    private async Task SyncMarks(params int[] markIds)
    {
        var syncServiceImpl = _sp.GetRequiredService<PathMarkSyncService>();
        await syncServiceImpl.SyncMarks(
            markIds.Length > 0 ? markIds : null,
            null,
            null,
            new PauseToken(),
            CancellationToken.None);
    }

    #region 不清除标记产生的影响 (cleanupEffects=false)

    /// <summary>
    /// 测试场景: 删除资源标记时不清除影响
    /// 预期: PathMark 和对应的 effect 记录被删除，但资源保持不变
    /// </summary>
    [TestMethod]
    public async Task DeleteWithoutCleanup_ResourceMark_KeepsResources()
    {
        // Arrange: 创建资源
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

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

        // 同步创建资源
        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().NotBeEmpty("Resources should be created after sync");

        var effectsBefore = await effectService.GetResourceEffectsByMarkId(mark.Id);
        effectsBefore.Should().NotBeEmpty("Resource effects should be recorded");

        // Act: 不清除影响地删除
        await pathMarkService.HardDeleteWithoutCleanup(mark.Id);

        // Assert: 资源仍然存在
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().HaveCount(resourcesBefore.Count, "Resources should not be deleted");

        // Assert: PathMark 已删除
        var markAfter = await pathMarkService.Get(mark.Id);
        markAfter.Should().BeNull("PathMark should be deleted");

        // Assert: Effect 记录已删除
        var effectsAfter = await effectService.GetResourceEffectsByMarkId(mark.Id);
        effectsAfter.Should().BeEmpty("Effect records should be cleaned up");
    }

    /// <summary>
    /// 测试场景: 删除属性标记时不清除影响
    /// 预期: PathMark 和 effect 记录被删除，属性值保持不变
    /// </summary>
    [TestMethod]
    public async Task DeleteWithoutCleanup_PropertyMark_KeepsPropertyValues()
    {
        // Arrange
        var seriesDir = Path.Combine(_testRoot, "Series1");
        var episodeDir = Path.Combine(seriesDir, "Episode1");
        Directory.CreateDirectory(episodeDir);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        // 创建属性
        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "KeepValueTest",
            Type = PropertyType.SingleLineText
        });

        // 添加资源标记
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

        // 添加属性标记
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
                ValueType = PropertyValueType.Fixed,
                FixedValue = "TestValue"
            }),
            Priority = 50
        });

        // 同步
        await SyncMarks();

        var resources = await resourceService.GetAll();
        resources.Should().NotBeEmpty();
        var resource = resources.First();

        var valuesBefore = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id);
        valuesBefore.Should().NotBeEmpty("Property values should exist after sync");

        // Act: 不清除影响地删除属性标记
        await pathMarkService.HardDeleteWithoutCleanup(propertyMark.Id);

        // Assert: 属性值仍然存在
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id);
        valuesAfter.Should().HaveCount(valuesBefore.Count, "Property values should not be deleted");
    }

    /// <summary>
    /// 测试场景: 按路径批量删除标记时不清除影响
    /// 预期: 所有标记和 effect 记录被删除，实际数据保持不变
    /// </summary>
    [TestMethod]
    public async Task DeleteByPathWithoutCleanup_KeepsAllData()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        // 添加多个标记
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
            Priority = 100
        });

        // 同步
        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().NotBeEmpty();

        // Act: 按路径删除所有标记（不清除影响）
        await pathMarkService.HardDeleteByPathWithoutCleanup(_testRoot);

        // Assert: 资源仍在
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().HaveCount(resourcesBefore.Count);

        // Assert: 标记已删除
        var marks = await pathMarkService.GetByPath(_testRoot, includeDeleted: true);
        marks.Should().BeEmpty();
    }

    #endregion

    #region 清除标记产生的影响 - 资源标记

    /// <summary>
    /// 测试场景: 删除资源标记并清除影响，资源不被其他标记覆盖
    /// 预期: 资源被删除
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_ResourceMark_NoSharing_DeletesResources()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
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

        await SyncMarks();
        (await resourceService.GetAll()).Should().NotBeEmpty();

        // Act: 软删除（触发 PendingDelete 同步）
        await pathMarkService.SoftDelete(mark.Id);
        await SyncMarks();

        // Assert: 资源已被删除
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().BeEmpty("Resources should be deleted when no other mark covers them");
    }

    /// <summary>
    /// 测试场景: 删除资源标记并清除影响，但资源被另一个资源标记覆盖
    /// 预期: 资源不被删除（共享保护）
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_ResourceMark_SharedByAnotherMark_KeepsResources()
    {
        // Arrange: 两个资源标记覆盖同一路径
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // 标记A: layer=1 匹配 Dir1
        var markA = await pathMarkService.Add(new PathMark
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

        // 标记B: 同样匹配 Dir1（不同路径，但 layer=0 直接指向 Dir1）
        var markB = await pathMarkService.Add(new PathMark
        {
            Path = dir1,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 50
        });

        // 同步两个标记
        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().ContainSingle(r => r.Path.EndsWith("Dir1"));

        // Act: 删除标记A
        await pathMarkService.SoftDelete(markA.Id);
        await SyncMarks();

        // Assert: 资源仍在（因为标记B仍覆盖 Dir1）
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().ContainSingle(r => r.Path.EndsWith("Dir1"),
            "Resource should NOT be deleted because markB still covers it");
    }

    /// <summary>
    /// 测试场景: 两个资源标记覆盖不同路径，删除标记A
    /// 预期: 标记A独有的资源被删除，标记B的资源不受影响
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_ResourceMark_PartialSharing_DeletesOnlyUnsharedResources()
    {
        // Arrange: 两个标记覆盖部分重叠的路径
        var dir1 = Path.Combine(_testRoot, "Dir1");
        var dir2 = Path.Combine(_testRoot, "Dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // 标记A: 覆盖所有一级子目录 (Dir1, Dir2)
        var markA = await pathMarkService.Add(new PathMark
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

        // 标记B: 只覆盖 Dir1
        var markB = await pathMarkService.Add(new PathMark
        {
            Path = dir1,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 50
        });

        // 同步
        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().HaveCount(2, "Both Dir1 and Dir2 should be resources");

        // Act: 删除标记A
        await pathMarkService.SoftDelete(markA.Id);
        await SyncMarks();

        // Assert: Dir1 仍在（被标记B覆盖），Dir2 被删除
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().ContainSingle(r => r.Path.EndsWith("Dir1"),
            "Dir1 should remain (covered by markB)");
        resourcesAfter.Should().NotContain(r => r.Path.EndsWith("Dir2"),
            "Dir2 should be deleted (only covered by deleted markA)");
    }

    /// <summary>
    /// 测试场景: 资源标记使用 MatchedAndSubdirectories 覆盖多层路径
    /// 删除后另一个更窄范围的标记仍然保护部分资源
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_ResourceMark_WideVsNarrowScope_CorrectProtection()
    {
        // Arrange
        var seriesDir = Path.Combine(_testRoot, "Series");
        var episodeDir = Path.Combine(seriesDir, "Episode1");
        Directory.CreateDirectory(episodeDir);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // 标记A: 宽范围 - 匹配一级子目录及其子目录
        var markA = await pathMarkService.Add(new PathMark
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

        // 标记B: 窄范围 - 只匹配 Episode1
        var markB = await pathMarkService.Add(new PathMark
        {
            Path = seriesDir,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 50
        });

        // 同步
        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().ContainSingle(r => r.Path.EndsWith("Episode1"));

        // Act: 删除宽范围标记A
        await pathMarkService.SoftDelete(markA.Id);
        await SyncMarks();

        // Assert: Episode1 仍在（被标记B覆盖）
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().ContainSingle(r => r.Path.EndsWith("Episode1"),
            "Episode1 should remain because markB still covers it");
    }

    #endregion

    #region 清除标记产生的影响 - 属性标记（标量属性）

    /// <summary>
    /// 测试场景: 删除属性标记（标量类型）并清除影响，没有其他属性标记覆盖
    /// 预期: 属性值被删除
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_PropertyMark_Scalar_NoSharing_DeletesValue()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "ScalarDeleteTest",
            Type = PropertyType.SingleLineText
        });

        // 资源标记
        await pathMarkService.Add(new PathMark
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

        // 属性标记
        var propertyMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "TestValue"
            }),
            Priority = 50
        });

        await SyncMarks();

        var resource = (await resourceService.GetAll()).First();
        var valuesBefore = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesBefore.Should().NotBeEmpty();

        // Act: 软删除属性标记
        await pathMarkService.SoftDelete(propertyMark.Id);
        await SyncMarks();

        // Assert: 属性值被删除
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesAfter.Should().BeEmpty("Property value should be deleted when no other mark covers it");
    }

    /// <summary>
    /// 测试场景: 删除属性标记（标量类型），但另一个标记也设置了相同属性
    /// 预期: 属性值保留（使用另一个标记的值）
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_PropertyMark_Scalar_SharedByAnotherMark_KeepsValue()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "ScalarSharedTest",
            Type = PropertyType.SingleLineText
        });

        // 资源标记
        await pathMarkService.Add(new PathMark
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

        // 属性标记A: 高优先级
        var propertyMarkA = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "ValueFromA"
            }),
            Priority = 80
        });

        // 属性标记B: 低优先级
        var propertyMarkB = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "ValueFromB"
            }),
            Priority = 50
        });

        await SyncMarks();

        var resource = (await resourceService.GetAll()).First();

        // Act: 删除标记A
        await pathMarkService.SoftDelete(propertyMarkA.Id);
        await SyncMarks();

        // Assert: 属性值仍然存在（来自标记B）
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesAfter.Should().NotBeEmpty("Property value from markB should remain");
        valuesAfter.First().Value.Should().Contain("ValueFromB",
            "Value should now come from the remaining markB");
    }

    #endregion

    #region 清除标记产生的影响 - 属性标记（集合属性）

    /// <summary>
    /// 测试场景: 删除集合属性标记时，仅删除该标记独有的值
    ///
    /// 例子:
    /// - 标记1 提供了 [a, b, c]
    /// - 标记2 提供了 [b, c, d]
    /// - 删除标记1 后，仅 "a" 被移除，最终值为 [b, c, d]
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_PropertyMark_Collection_RemovesOnlyUniqueValues()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "CollectionDeleteTest",
            Type = PropertyType.MultipleChoice
        });

        // 资源标记
        await pathMarkService.Add(new PathMark
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

        // 属性标记A: 提供 [a, b, c]
        var propertyMarkA = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "a,b,c"
            }),
            Priority = 80
        });

        // 属性标记B: 提供 [b, c, d]
        var propertyMarkB = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "b,c,d"
            }),
            Priority = 50
        });

        await SyncMarks();

        var resource = (await resourceService.GetAll()).First();
        var valuesBefore = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesBefore.Should().NotBeEmpty();
        // 合并后应包含 [a, b, c, d]
        var combinedValueBefore = valuesBefore.First().Value;
        combinedValueBefore.Should().Contain("a");
        combinedValueBefore.Should().Contain("b");
        combinedValueBefore.Should().Contain("c");
        combinedValueBefore.Should().Contain("d");

        // Act: 删除标记A
        await pathMarkService.SoftDelete(propertyMarkA.Id);
        await SyncMarks();

        // Assert: 仅 "a" 被移除，[b, c, d] 保留
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesAfter.Should().NotBeEmpty("Combined value from markB should remain");

        var combinedValueAfter = valuesAfter.First().Value!;
        combinedValueAfter.Should().NotContain("a", "Value 'a' should be removed (only from markA)");
        combinedValueAfter.Should().Contain("b", "Value 'b' should remain (also in markB)");
        combinedValueAfter.Should().Contain("c", "Value 'c' should remain (also in markB)");
        combinedValueAfter.Should().Contain("d", "Value 'd' should remain (from markB)");
    }

    /// <summary>
    /// 测试场景: 删除唯一的集合属性标记
    /// 预期: 整个属性值被删除
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_PropertyMark_Collection_OnlyMark_DeletesEntireValue()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "CollectionSoleDeleteTest",
            Type = PropertyType.MultipleChoice
        });

        // 资源标记
        await pathMarkService.Add(new PathMark
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

        // 唯一的属性标记
        var propertyMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "x,y,z"
            }),
            Priority = 50
        });

        await SyncMarks();

        var resource = (await resourceService.GetAll()).First();
        var valuesBefore = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesBefore.Should().NotBeEmpty();

        // Act: 删除唯一的属性标记
        await pathMarkService.SoftDelete(propertyMark.Id);
        await SyncMarks();

        // Assert: 整个属性值被删除
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesAfter.Should().BeEmpty("Entire property value should be deleted when sole mark is removed");
    }

    #endregion

    #region 清除标记产生的影响 - 媒体库标记

    /// <summary>
    /// 测试场景: 删除媒体库标记并清除影响，没有其他标记
    /// 预期: 媒体库-资源映射被删除
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_MediaLibraryMark_NoSharing_DeletesMapping()
    {
        // Arrange
        var seriesDir = Path.Combine(_testRoot, "Series");
        var episodeDir = Path.Combine(seriesDir, "Episode1");
        Directory.CreateDirectory(episodeDir);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var mediaLibraryService = _sp.GetRequiredService<IMediaLibraryService>();
        var mappingService = _sp.GetRequiredService<IMediaLibraryResourceMappingService>();

        // 创建媒体库
        var testLibrary = new MediaLibrary { Name = "DeleteMappingTest" };
        await mediaLibraryService.AddRange(new[] { testLibrary });
        var libraries = await mediaLibraryService.GetAll();
        testLibrary = libraries.First(l => l.Name == "DeleteMappingTest");

        // 资源标记
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

        // 媒体库标记
        var mlMark = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                ValueType = PropertyValueType.Fixed,
                MediaLibraryId = testLibrary.Id
            }),
            Priority = 50
        });

        await SyncMarks();

        var resources = await resourceService.GetAll();
        resources.Should().NotBeEmpty();
        var resource = resources.First();

        var mappingsBefore = await mappingService.GetMediaLibraryIdsByResourceId(resource.Id);
        mappingsBefore.Should().Contain(testLibrary.Id, "Media library mapping should exist after sync");

        // Act: 删除媒体库标记
        await pathMarkService.SoftDelete(mlMark.Id);
        await SyncMarks();

        // Assert: 映射被删除
        var resourceAfter = (await resourceService.GetAll()).FirstOrDefault();
        if (resourceAfter != null)
        {
            var mappingsAfter = await mappingService.GetMediaLibraryIdsByResourceId(resourceAfter.Id);
            mappingsAfter.Should().NotContain(testLibrary.Id,
                "Media library mapping should be deleted when the mark is removed");
        }
    }

    #endregion

    #region 边界情况

    /// <summary>
    /// 测试场景: 资源标记使用不同的路径和匹配模式覆盖同一资源
    /// 验证跨路径的共享保护
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_ResourceMark_CrossPathSharing_KeepsSharedResources()
    {
        // Arrange: 创建嵌套目录
        var parentDir = Path.Combine(_testRoot, "Parent");
        var childDir = Path.Combine(parentDir, "Child");
        Directory.CreateDirectory(childDir);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();

        // 标记A: 从 _testRoot 匹配 layer=2 (即 Child)
        var markA = await pathMarkService.Add(new PathMark
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

        // 标记B: 从 Parent 匹配 layer=1 (也是 Child)
        var markB = await pathMarkService.Add(new PathMark
        {
            Path = parentDir,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 50
        });

        await SyncMarks();
        var resourcesBefore = await resourceService.GetAll();
        resourcesBefore.Should().ContainSingle(r => r.Path.EndsWith("Child"));

        // Act: 删除标记A
        await pathMarkService.SoftDelete(markA.Id);
        await SyncMarks();

        // Assert: Child 仍在（被标记B覆盖）
        var resourcesAfter = await resourceService.GetAll();
        resourcesAfter.Should().ContainSingle(r => r.Path.EndsWith("Child"),
            "Child should remain because markB from Parent path still covers it");
    }

    /// <summary>
    /// 测试场景: 属性标记来自不同路径但覆盖同一资源的同一属性
    /// 验证跨路径的属性共享保护
    /// </summary>
    [TestMethod]
    public async Task DeleteWithCleanup_PropertyMark_CrossPathSharing_KeepsSharedValues()
    {
        // Arrange
        var dir1 = Path.Combine(_testRoot, "Dir1");
        Directory.CreateDirectory(dir1);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var customPropertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();

        var testProperty = await customPropertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = "CrossPathPropertyTest",
            Type = PropertyType.SingleLineText
        });

        // 资源标记
        await pathMarkService.Add(new PathMark
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

        // 属性标记A: 从 _testRoot 设置属性
        var propertyMarkA = await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "ValueA"
            }),
            Priority = 80
        });

        // 属性标记B: 从 Dir1 设置相同属性
        var propertyMarkB = await pathMarkService.Add(new PathMark
        {
            Path = dir1,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = testProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "ValueB"
            }),
            Priority = 50
        });

        await SyncMarks();

        var resource = (await resourceService.GetAll()).First();

        // Act: 删除标记A
        await pathMarkService.SoftDelete(propertyMarkA.Id);
        await SyncMarks();

        // Assert: 属性值仍在（来自标记B）
        var valuesAfter = await propertyValueService.GetAllDbModels(
            v => v.ResourceId == resource.Id && v.PropertyId == testProperty.Id &&
                 v.Scope == (int)PropertyValueScope.Synchronization);
        valuesAfter.Should().NotBeEmpty(
            "Property value should remain because markB from Dir1 still provides it");
        valuesAfter.First().Value.Should().Contain("ValueB");
    }

    #endregion
}
