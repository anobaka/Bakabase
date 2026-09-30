using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Every path that writes a custom property row must keep the columns it was not asked to change:
/// <c>CreatedAt</c> (the ID-reuse fingerprint of data sync, compared by ticks) and <c>Order</c> (where
/// the user put the property). <c>ChangeType</c> used to persist <c>Property.ToCustomProperty()</c>
/// through <c>Put(CustomProperty)</c>, which carries neither, so a type change reset
/// <c>CreatedAt</c> to <see cref="DateTime.MinValue"/> and moved the property to the front.
/// Each case checks the service's view and the stored row, read past the memory cache.
/// </summary>
[TestClass]
public sealed class CustomPropertyWritePathTests
{
    // Sub-millisecond ticks on purpose: the fingerprint is CreatedAt.Ticks, so it must round-trip exactly.
    private static readonly DateTime KnownCreatedAt = new DateTime(2021, 3, 4, 5, 6, 7, 890).AddTicks(1234);
    private const int KnownOrder = 5;
    private const string CaptureRegex = @"\[(?<studio>[^\]]+)\]";

    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    private ICustomPropertyService Properties => _sp.GetRequiredService<ICustomPropertyService>();

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(Path.GetTempPath(), $"CustomPropertyWritePathTests.{Guid.NewGuid():N}");
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

    #region Helpers

    /// <summary>
    /// Adds the property under test, pins its <c>CreatedAt</c> to <see cref="KnownCreatedAt"/> (Add
    /// stamps the current time, which an accidental rewrite could reproduce) and moves it to position
    /// <see cref="KnownOrder"/> with <c>Sort</c>, behind five other properties.
    /// </summary>
    private async Task<int> AddSubject(string name, PropertyType type, object? options = null)
    {
        var subject = await Properties.Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = type,
            Options = options == null ? null : JsonConvert.SerializeObject(options)
        });
        var row = (await Properties.GetByKey(subject.Id)).ToDbModel() with { CreatedAt = KnownCreatedAt };
        await Properties.UpdateRange([row]);

        var others = await Properties.AddRange(Enumerable.Range(0, KnownOrder)
            .Select(i => new CustomPropertyAddOrPutDto { Name = $"Other {i}", Type = PropertyType.SingleLineText })
            .ToArray());
        await Properties.Sort([..others.Select(o => o.Id), subject.Id]);

        await AssertCreatedAtAndOrderKept(subject.Id);
        return subject.Id;
    }

    /// <summary>Asserts the known <c>CreatedAt</c> and the expected <c>Order</c> in the service and in the database.</summary>
    private async Task<CustomPropertyDbModel> AssertCreatedAtAndOrderKept(int id, int expectedOrder = KnownOrder)
    {
        var viewed = await Properties.GetByKey(id);
        viewed.CreatedAt.Ticks.Should().Be(KnownCreatedAt.Ticks, "the service must keep the creation time");
        viewed.Order.Should().Be(expectedOrder, "the service must keep the order");

        await using var scope = _sp.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().CustomProperties
            .AsNoTracking().SingleAsync(p => p.Id == id);
        stored.CreatedAt.Ticks.Should().Be(KnownCreatedAt.Ticks, "the stored row must keep the creation time");
        stored.Order.Should().Be(expectedOrder, "the stored row must keep the order");
        return stored;
    }

    private async Task<int> AddResource(string dirName)
    {
        var path = Path.Combine(_testRoot, dirName);
        Directory.CreateDirectory(path);
        var resources = _sp.GetRequiredService<IResourceService>();
        await resources.AddOrPutRange([new Resource { Path = path, IsFile = false }]);
        return (await resources.GetAll()).Single(r => r.FileName == dirName).Id;
    }

    private async Task AddManualValue(int resourceId, int propertyId, PropertyType type, object dbValue)
        => await _sp.GetRequiredService<ICustomPropertyValueService>().AddDbModelRange([
            new CustomPropertyValueDbModel
            {
                ResourceId = resourceId,
                PropertyId = propertyId,
                Scope = (int) PropertyValueScope.Manual,
                Value = dbValue.SerializeAsStandardValue(type.GetDbValueType())
            }
        ]);

    private async Task<object?> BizValueOf(int resourceId, int propertyId)
        => (await _sp.GetRequiredService<ICustomPropertyValueService>().GetAll(
                v => v.ResourceId == resourceId && v.PropertyId == propertyId,
                CustomPropertyValueAdditionalItem.BizValue, false))
            .Should().ContainSingle().Subject.BizValue;

    private static IEnumerable<string> ChoiceLabels(object? options) => options switch
    {
        SingleChoicePropertyOptions s => s.Choices?.Select(c => c.Label) ?? [],
        MultipleChoicePropertyOptions m => m.Choices?.Select(c => c.Label) ?? [],
        _ => []
    };

    /// <summary>Resource sync, then path mark sync (the same two steps as <c>PathMarkSyncTests</c>).</summary>
    private async Task SyncPathMarks()
    {
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        await _sp.GetRequiredService<PathMarkSyncService>().SyncMarks(
            null, null, new PauseToken(), CancellationToken.None);
    }

    #endregion

    [TestMethod]
    public async Task PutById_KeepsCreatedAtAndOrder()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);

        await Properties.Put(id, new CustomPropertyAddOrPutDto { Name = "Renamed", Type = PropertyType.SingleLineText });

        (await AssertCreatedAtAndOrderKept(id)).Name.Should().Be("Renamed");
    }

    [TestMethod]
    public async Task PutDomainModelLoadedFromGetByKey_KeepsCreatedAtAndOrder()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);

        var property = await Properties.GetByKey(id);
        property.Name = "Renamed";
        await Properties.Put(property);

        (await AssertCreatedAtAndOrderKept(id)).Name.Should().Be("Renamed");
    }

    [TestMethod]
    public async Task Sort_ChangesOrderAsAskedButNotCreatedAt()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);
        var ids = (await Properties.GetAll()).OrderBy(p => p.Order).Select(p => p.Id).Where(x => x != id).ToList();
        ids.Insert(2, id);

        await Properties.Sort(ids.ToArray());

        await AssertCreatedAtAndOrderKept(id, expectedOrder: 2);
    }

    [TestMethod]
    public async Task SetOrders_ChangesOrderAsAskedButNotCreatedAt()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);

        await Properties.SetOrders(new Dictionary<int, int> { [id] = 2 });

        (await AssertCreatedAtAndOrderKept(id, expectedOrder: 2)).Name.Should().Be("Title");
    }

    /// <summary>The data sync row: an update through the custom property adapter (<c>Put</c> by id).</summary>
    [TestMethod]
    public async Task DataSyncUpdate_KeepsCreatedAtAndOrder()
    {
        var id = await AddSubject("Genre", PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            Choices = [new() { Label = "Action", Value = "uuid-action" }]
        });
        var kind = DataSyncKind();
        var local = (await kind.ReadAsync([id.ToString()], CancellationToken.None)).Single();
        var content = (CustomPropertyContentV1) kind.Codec.ReadLocal(local.Content);
        var merged = content with
        {
            Name = "Genres", Choices = [..content.Choices, new CustomPropertyChoiceV1("uuid-drama", "Drama", null)]
        };

        var outcome = await kind.ApplyAsync(new ApplyBatch(DataSyncKindIds.CustomProperty,
        [
            new UpdateEntityOperation("u", id.ToString(), ContentHash.Of(local.Content), kind.Codec.Write(merged),
                EntityKeys.None, ["uuid-drama"], [])
        ]), CancellationToken.None);

        outcome.ChangedDuringApplyItemIds.Should().BeEmpty();
        (await AssertCreatedAtAndOrderKept(id)).Name.Should().Be("Genres");
        ChoiceLabels((await Properties.GetByKey(id)).Options).Should().Equal("Action", "Drama");
    }

    /// <summary>The data sync row: a subtype change through the custom property adapter (<c>ChangeType</c>).</summary>
    [TestMethod]
    public async Task DataSyncSubtypeChange_KeepsCreatedAtOrderAndName()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);
        var resourceId = await AddResource("r1");
        await AddManualValue(resourceId, id, PropertyType.SingleLineText, "Hello");

        await DataSyncKind().ChangeSubtypeAsync(id.ToString(), nameof(PropertyType.MultilineText), CancellationToken.None);

        var stored = await AssertCreatedAtAndOrderKept(id);
        stored.Name.Should().Be("Title");
        stored.Type.Should().Be(PropertyType.MultilineText);
        (await BizValueOf(resourceId, id)).Should().Be("Hello");
    }

    private IDataSyncKind DataSyncKind() => _sp.GetServices<IDataSyncKind>()
        .Single(k => k.Codec.Descriptor.Kind == DataSyncKindIds.CustomProperty);

    [TestMethod]
    public async Task ChangeType_Lossless_KeepsCreatedAtOrderAndName()
    {
        var id = await AddSubject("Title", PropertyType.SingleLineText);
        var resourceId = await AddResource("r1");
        await AddManualValue(resourceId, id, PropertyType.SingleLineText, "Hello");

        await Properties.ChangeType(id, PropertyType.MultilineText);

        var stored = await AssertCreatedAtAndOrderKept(id);
        stored.Name.Should().Be("Title");
        stored.Type.Should().Be(PropertyType.MultilineText);
        stored.Options.Should().BeNull("text properties have no options");
        (await BizValueOf(resourceId, id)).Should().Be("Hello");
    }

    [TestMethod]
    public async Task ChangeType_Lossy_KeepsCreatedAtOrderAndName_AndConvertsOptions()
    {
        var id = await AddSubject("Genre", PropertyType.MultipleChoice, new MultipleChoicePropertyOptions
        {
            IgnoreCase = true,
            Choices =
            [
                new() { Label = "Action", Value = "uuid-action" },
                new() { Label = "Comedy", Value = "uuid-comedy" },
                new() { Label = "Drama", Value = "uuid-drama" }
            ]
        });
        var single = await AddResource("single");
        var both = await AddResource("both");
        await AddManualValue(single, id, PropertyType.MultipleChoice, new List<string> { "uuid-action" });
        await AddManualValue(both, id, PropertyType.MultipleChoice, new List<string> { "uuid-action", "uuid-comedy" });

        await Properties.ChangeType(id, PropertyType.SingleChoice);

        var stored = await AssertCreatedAtAndOrderKept(id);
        stored.Name.Should().Be("Genre");
        stored.Type.Should().Be(PropertyType.SingleChoice);

        // The new options are the ones the values converted into: the list collapses into one label,
        // the unused choice is gone, and IgnoreCase carries over.
        var options = (await Properties.GetByKey(id)).Options.Should().BeOfType<SingleChoicePropertyOptions>().Subject;
        options.IgnoreCase.Should().BeTrue();
        ChoiceLabels(options).Should().BeEquivalentTo("Action", "Action,Comedy");
        (await BizValueOf(single, id)).Should().Be("Action");
        (await BizValueOf(both, id)).Should().Be("Action,Comedy");
    }

    [TestMethod]
    public async Task CustomPropertyValueServiceBizValuePut_CreatesOptions_KeepsCreatedAtAndOrder()
    {
        var id = await AddSubject("Genre", PropertyType.MultipleChoice);
        var resourceId = await AddResource("r1");

        await _sp.GetRequiredService<ICustomPropertyValueService>().SaveByResources([
            new Resource
            {
                Id = resourceId,
                Properties = new()
                {
                    [(int) PropertyPool.Custom] = new()
                    {
                        [id] = new Resource.Property("Genre", PropertyType.MultipleChoice,
                        [
                            new Resource.Property.PropertyValue((int) PropertyValueScope.Manual, null,
                                new List<string> { "Action" }, null)
                        ])
                    }
                }
            }
        ]);

        await AssertCreatedAtAndOrderKept(id);
        ChoiceLabels((await Properties.GetByKey(id)).Options).Should().BeEquivalentTo("Action");
        (await BizValueOf(resourceId, id)).Should().BeEquivalentTo(new List<string> { "Action" });
    }

    [TestMethod]
    public async Task ResourceServiceBizValuePut_CreatesOptions_KeepsCreatedAtAndOrder()
    {
        var id = await AddSubject("Genre", PropertyType.MultipleChoice);
        var resourceId = await AddResource("r1");

        await _sp.GetRequiredService<IResourceService>().PutPropertyValue(resourceId,
            new ResourcePropertyValuePutInputModel
            {
                PropertyId = id,
                IsCustomProperty = true,
                IsBizValue = true,
                Value = new List<string> { "Action" }.SerializeAsStandardValue(StandardValueType.ListString)
            });

        await AssertCreatedAtAndOrderKept(id);
        ChoiceLabels((await Properties.GetByKey(id)).Options).Should().BeEquivalentTo("Action");
        (await BizValueOf(resourceId, id)).Should().BeEquivalentTo(new List<string> { "Action" });
    }

    [TestMethod]
    public async Task PathMarkSyncOptionCreation_KeepsCreatedAtAndOrder()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, "Action", "Movie1"));
        var id = await AddSubject("Genre", PropertyType.MultipleChoice);
        var pathMarks = _sp.GetRequiredService<IPathMarkService>();
        await pathMarks.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                FsTypeFilter = PathFilterFsType.Directory,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 100
        });
        // The value is the first-layer directory name, which is not a choice yet.
        await pathMarks.Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 2,
                Pool = PropertyPool.Custom,
                PropertyId = id,
                ValueType = PropertyValueType.Dynamic,
                ValueLayer = 1,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 50
        });

        await SyncPathMarks();

        await AssertCreatedAtAndOrderKept(id);
        ChoiceLabels((await Properties.GetByKey(id)).Options).Should().BeEquivalentTo("Action");
    }

    [TestMethod]
    public async Task EnhancerOptionGrowth_KeepsCreatedAtAndOrder()
    {
        // The same network-free Regex enhancer setup as EnhancerOrchestrationTests.
        Directory.CreateDirectory(Path.Combine(_testRoot, "[Toei] My Show"));
        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
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
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        var resourceId = (await _sp.GetRequiredService<IResourceService>().GetAll()).Single().Id;

        var id = await AddSubject("Studio", PropertyType.MultipleChoice);
        await _sp.GetRequiredService<IResourceProfileService>().Add(
            "enhance", "{}", null,
            new ResourceProfileEnhancerOptions
            {
                Enhancers =
                [
                    new EnhancerFullOptions
                    {
                        EnhancerId = (int) EnhancerId.Regex,
                        TargetOptions =
                        [
                            new EnhancerTargetFullOptions
                            {
                                Target = (int) RegexEnhancerTarget.CaptureGroups,
                                DynamicTarget = "studio",
                                PropertyPool = PropertyPool.Custom,
                                PropertyId = id
                            }
                        ]
                    }
                ]
            },
            null, null, null, 100);
        await _sp.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);

        await _sp.GetRequiredService<IEnhancerService>().EnhanceResourceWithOptions(
            resourceId,
            [new EnhancerFullOptions { EnhancerId = (int) EnhancerId.Regex, Expressions = [CaptureRegex] }],
            CancellationToken.None);

        await AssertCreatedAtAndOrderKept(id);
        ChoiceLabels((await Properties.GetByKey(id)).Options).Should().BeEquivalentTo("Toei");
    }
}
