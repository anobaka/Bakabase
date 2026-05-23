using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for priority-conflict resolution when two path marks write the same property
/// to the same resource. The existing PathMarkSyncTests only asserts both marks reach
/// Synced — none verify which value actually wins.
/// </summary>
[TestClass]
public sealed class PathMarkPriorityConflictTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkPrio.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private async Task Sync()
    {
        await _sp.GetRequiredService<ResourceSyncService>()
            .SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        await _sp.GetRequiredService<PathMarkSyncService>()
            .SyncMarks(null, null, new PauseToken(), CancellationToken.None);
    }

    [TestMethod]
    public async Task TwoPropertyMarks_SameProperty_HigherPriorityValueWins()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, "A"));

        var prop = await _sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        {
            Name = "TestProp",
            Type = PropertyType.SingleLineText
        });

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
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
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = prop.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "LowPriorityValue"
            }),
            Priority = 10
        });
        await pathMarkService.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = prop.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "HighPriorityValue"
            }),
            Priority = 200
        });

        await Sync();

        var values = await _sp.GetRequiredService<ICustomPropertyValueService>()
            .GetAll(x => x.PropertyId == prop.Id, CustomPropertyValueAdditionalItem.BizValue, true);
        Assert.AreEqual(1, values.Count,
            "Two marks write the same (resource, property) — only one CustomPropertyValue row should exist");
        Assert.AreEqual("HighPriorityValue", values[0].BizValue);
    }
}
