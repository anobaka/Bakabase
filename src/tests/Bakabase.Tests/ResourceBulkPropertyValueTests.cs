using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceBulkPropertyValueTests
{
    private const string OldId = "uuid-old";
    private const string FirstId = "uuid-first";
    private const string SecondId = "uuid-second";
    private const string FirstLabel = "Action,Comedy";
    private const string SecondLabel = "Drama;Fantasy";
    private const string TagGroup = "Series, A";
    private IServiceProvider _services = null!;
    private int _propertyId;
    private int[] _resourceIds = [];

    private IResourceService Resources => _services.GetRequiredService<IResourceService>();
    private ICustomPropertyService Properties => _services.GetRequiredService<ICustomPropertyService>();
    private ICustomPropertyValueService Values => _services.GetRequiredService<ICustomPropertyValueService>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    [TestMethod]
    [DataRow(PropertyType.SingleChoice)]
    [DataRow(PropertyType.MultipleChoice)]
    [DataRow(PropertyType.Tags)]
    [DataRow(PropertyType.Number)]
    [DataRow(PropertyType.Boolean)]
    public async Task BulkPutDbValue_AddsUpdatesAndClearsManualValues(PropertyType type)
    {
        await Seed(type, withOptions: true);
        var existingId = (await Values.GetAllDbModels(v =>
            v.PropertyId == _propertyId && v.Scope == (int)PropertyValueScope.Manual)).Single().Id;
        var serialized = NewDbValue(type).SerializeAsStandardValue(type.GetDbValueType());

        var result = await Resources.BulkPutPropertyValue(_resourceIds, Input(serialized));

        result.Code.Should().Be(0);
        await AssertManualValues(serialized, NewBizValue(type));
        (await Values.GetAllDbModels(v => v.Id == existingId)).Should().ContainSingle();
        await AssertSynchronizationValues(type);

        var clearResult = await Resources.BulkPutPropertyValue(_resourceIds, Input(null));

        clearResult.Code.Should().Be(0);
        await AssertManualValues(null, null);
        await AssertSynchronizationValues(type);
    }

    [TestMethod]
    [DataRow(PropertyType.SingleChoice)]
    [DataRow(PropertyType.MultipleChoice)]
    [DataRow(PropertyType.Tags)]
    public async Task BulkPutBizValue_CreatesOptionsAndPersistsTheirIds(PropertyType type)
    {
        await Seed(type, withOptions: false);
        var bizValue = NewBizValue(type);
        var serialized = bizValue.SerializeAsStandardValue(type.GetBizValueType());

        var result = await Resources.BulkPutPropertyValue(_resourceIds, Input(serialized, isBizValue: true));

        result.Code.Should().Be(0);
        var property = await Properties.GetByKey(_propertyId);
        object expectedDbValue = property.Options switch
        {
            SingleChoicePropertyOptions single => single.Choices!.Single(c => c.Label == FirstLabel).Value,
            MultipleChoicePropertyOptions multiple => new List<string>
            {
                multiple.Choices!.Single(c => c.Label == FirstLabel).Value,
                multiple.Choices!.Single(c => c.Label == SecondLabel).Value
            },
            TagsPropertyOptions tags => new List<string>
            {
                tags.Tags!.Single(t => t.Group == TagGroup && t.Name == FirstLabel).Value,
                tags.Tags!.Single(t => t.Group == null && t.Name == SecondLabel).Value
            },
            _ => throw new InvalidOperationException("Bulk business values must create reference options.")
        };
        await AssertManualValues(expectedDbValue.SerializeAsStandardValue(type.GetDbValueType()), bizValue);
    }

    private ResourcePropertyValuePutInputModel Input(string? value, bool isBizValue = false) => new()
    {
        PropertyId = _propertyId,
        IsCustomProperty = true,
        IsBizValue = isBizValue,
        Value = value
    };

    private async Task Seed(PropertyType type, bool withOptions)
    {
        object? options = !withOptions ? null : type switch
        {
            PropertyType.SingleChoice => new SingleChoicePropertyOptions
            {
                Choices = [new() { Label = "Old", Value = OldId }, new() { Label = FirstLabel, Value = FirstId }]
            },
            PropertyType.MultipleChoice => new MultipleChoicePropertyOptions
            {
                Choices = [new() { Label = "Old", Value = OldId }, new() { Label = FirstLabel, Value = FirstId },
                    new() { Label = SecondLabel, Value = SecondId }]
            },
            PropertyType.Tags => new TagsPropertyOptions
            {
                Tags = [new(null, "Old") { Value = OldId }, new(TagGroup, FirstLabel) { Value = FirstId },
                    new(null, SecondLabel) { Value = SecondId }]
            },
            _ => null
        };
        _propertyId = (await Properties.Add(new CustomPropertyAddOrPutDto
        {
            Name = "Bulk property",
            Type = type,
            Options = options == null ? null : JsonConvert.SerializeObject(options)
        })).Id;
        var root = Path.Combine(Path.GetTempPath(), $"bulk-property-{Guid.NewGuid():N}");
        await Resources.AddOrPutRange([
            new Resource { Path = Path.Combine(root, "existing"), IsFile = false },
            new Resource { Path = Path.Combine(root, "new"), IsFile = false }
        ]);
        _resourceIds = (await Resources.GetAll()).OrderBy(r => r.FileName).Select(r => r.Id).ToArray();

        // One resource exercises an update, the other an insert. Other scopes must stay intact.
        var oldValue = OldDbValue(type).SerializeAsStandardValue(type.GetDbValueType());
        await Values.AddDbModelRange([
            new CustomPropertyValueDbModel
            {
                ResourceId = _resourceIds[0], PropertyId = _propertyId,
                Scope = (int)PropertyValueScope.Manual, Value = oldValue
            },
            .._resourceIds.Select(id => new CustomPropertyValueDbModel
            {
                ResourceId = id, PropertyId = _propertyId,
                Scope = (int)PropertyValueScope.Synchronization, Value = oldValue
            })
        ]);
    }

    private async Task AssertManualValues(string? expectedDbValue, object? expectedBizValue)
    {
        var cached = await Values.GetAllDbModels(v =>
            v.PropertyId == _propertyId && v.Scope == (int)PropertyValueScope.Manual);
        cached.Should().HaveCount(2).And.OnlyContain(v => v.Value == expectedDbValue);
        cached.Select(v => v.ResourceId).Should().BeEquivalentTo(_resourceIds);

        await using var scope = _services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>().CustomPropertyValues
            .AsNoTracking().Where(v => v.PropertyId == _propertyId && v.Scope == (int)PropertyValueScope.Manual)
            .ToListAsync();
        stored.Should().HaveCount(2).And.OnlyContain(v => v.Value == expectedDbValue);

        foreach (var resourceId in _resourceIds)
        {
            var resource = await Resources.Get(resourceId, ResourceAdditionalItem.Properties);
            var manual = resource!.Properties![(int)PropertyPool.Custom][_propertyId].Values!
                .Single(v => v.Scope == (int)PropertyValueScope.Manual);
            manual.BizValue.Should().BeEquivalentTo(expectedBizValue);
        }
    }

    private async Task AssertSynchronizationValues(PropertyType type)
    {
        var oldValue = OldDbValue(type).SerializeAsStandardValue(type.GetDbValueType());
        var values = await Values.GetAllDbModels(v =>
            v.PropertyId == _propertyId && v.Scope == (int)PropertyValueScope.Synchronization);
        values.Should().HaveCount(2).And.OnlyContain(v => v.Value == oldValue);
    }

    private static object OldDbValue(PropertyType type) => type switch
    {
        PropertyType.SingleChoice => OldId,
        PropertyType.Number => 1m,
        PropertyType.Boolean => false,
        _ => new List<string> { OldId }
    };

    private static object NewDbValue(PropertyType type) => type switch
    {
        PropertyType.SingleChoice => FirstId,
        PropertyType.Number => 4.25m,
        PropertyType.Boolean => true,
        _ => new List<string> { FirstId, SecondId }
    };

    private static object NewBizValue(PropertyType type) => type switch
    {
        PropertyType.SingleChoice => FirstLabel,
        PropertyType.MultipleChoice => new List<string> { FirstLabel, SecondLabel },
        PropertyType.Tags => new List<TagValue> { new(TagGroup, FirstLabel), new(null, SecondLabel) },
        PropertyType.Number => 4.25m,
        PropertyType.Boolean => true,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
