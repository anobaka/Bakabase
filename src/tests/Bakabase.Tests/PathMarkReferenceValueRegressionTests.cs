using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business;
using Bootstrap.Components.Orm;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Controllers;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Regression coverage for the canonical business-value representation of
/// <see cref="PropertyMarkEffect.Value"/> and the legacy V220 rows that were incorrectly
/// seeded with serialized database values. A partial sync must not reinterpret an old option
/// id as a label and create a second reference whose label is that id.
/// </summary>
[TestClass]
public sealed class PathMarkReferenceValueRegressionTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkReferenceValueRegressionTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, true);
            }
            catch
            {
                // Best-effort cleanup. A failed assertion should remain the test's failure.
            }
        }
    }

    [TestMethod]
    public async Task SearchInput_SingleChoiceIn_DeserializesSerializedListStringAtTheApiBoundary()
    {
        var choiceId = Guid.NewGuid().ToString();
        var property = await AddProperty(
            "Single choice API filter",
            PropertyType.SingleChoice,
            new SingleChoicePropertyOptions
            {
                Choices = [new ChoiceOptions {Label = "Action", Value = choiceId}]
            });

        var search = await BuildSingleChoiceInApiSearch(property.Id, choiceId);

        var filter = search.Group!.Filters.Should().ContainSingle().Subject;
        filter.Property.Should().NotBeNull();
        filter.Property!.Type.Should().Be(PropertyType.SingleChoice);
        filter.Operation.Should().Be(SearchOperation.In);
        filter.DbValue.Should().BeOfType<List<string>>().Subject.Should().Equal(choiceId);
    }

    [TestMethod]
    public async Task PartialSync_V220ReferenceEffectsCanonicalizeWithoutChangingDbValues_AndFiltersMatch()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        // The reported production case extracts the value from a directory layer.
        // Keep that exact path in the single-choice case; the other reference types share
        // the same resource while exercising their distinct standard-value shapes.
        var resourcePath = Path.Combine(_testRoot, "Action");
        Directory.CreateDirectory(resourcePath);
        await resourceService.AddOrPutRange([
            new Resource
            {
                Path = resourcePath,
                IsFile = false
            }
        ]);
        var resource = (await resourceService.GetAll()).Should().ContainSingle().Subject;

        var singleChoiceId = Guid.NewGuid().ToString();
        var multipleChoiceId = Guid.NewGuid().ToString();
        var tagId = Guid.NewGuid().ToString();
        var multilevelRootId = Guid.NewGuid().ToString();
        var multilevelLeafId = Guid.NewGuid().ToString();

        var singleChoice = await AddProperty(
            "Legacy single choice",
            PropertyType.SingleChoice,
            new SingleChoicePropertyOptions
            {
                Choices = [new ChoiceOptions {Label = "Action", Value = singleChoiceId}]
            });
        var multipleChoice = await AddProperty(
            "Legacy multiple choice",
            PropertyType.MultipleChoice,
            new MultipleChoicePropertyOptions
            {
                Choices = [new ChoiceOptions {Label = "Comedy", Value = multipleChoiceId}]
            });
        var tags = await AddProperty(
            "Legacy tags",
            PropertyType.Tags,
            new TagsPropertyOptions
            {
                Tags = [new TagsPropertyOptions.TagOptions("Genre", "Drama") {Value = tagId}]
            });
        var multilevel = await AddProperty(
            "Legacy multilevel",
            PropertyType.Multilevel,
            new MultilevelPropertyOptions
            {
                Data =
                [
                    new MultilevelDataOptions
                    {
                        Label = "Region",
                        Value = multilevelRootId,
                        Children =
                        [
                            new MultilevelDataOptions
                            {
                                Label = "Japan",
                                Value = multilevelLeafId
                            }
                        ]
                    }
                ]
            });

        var legacyCases = new[]
        {
            (Property: singleChoice, SerializedDbValue: singleChoiceId, CanonicalEffectValue: "Action"),
            (Property: multipleChoice, SerializedDbValue: multipleChoiceId, CanonicalEffectValue: "Comedy"),
            (Property: tags, SerializedDbValue: tagId, CanonicalEffectValue: "Genre,Drama"),
            (Property: multilevel, SerializedDbValue: multilevelLeafId, CanonicalEffectValue: "Region,Japan")
        };
        var contextMarkIds = new Dictionary<int, int>();

        foreach (var (property, serializedDbValue, canonicalEffectValue) in legacyCases)
        {
            // V220 created a Synced mark plus an effect copied verbatim from
            // CustomPropertyValues.Value.  It did not collect the path label again.
            var contextMark = property.Id == singleChoice.Id
                ? await AddLayerPropertyMark(property.Id)
                : await AddPropertyMark(property.Id, canonicalEffectValue);
            await pathMarkService.MarkAsSynced(contextMark.Id);
            contextMarkIds[property.Id] = contextMark.Id;

            await propertyValueService.AddDbModelRange([
                new CustomPropertyValueDbModel
                {
                    ResourceId = resource.Id,
                    PropertyId = property.Id,
                    Scope = (int) PropertyValueScope.Synchronization,
                    Value = serializedDbValue
                }
            ]);
            await effectService.AddPropertyEffects([
                new PropertyMarkEffect
                {
                    MarkId = contextMark.Id,
                    PropertyPool = PropertyPool.Custom,
                    PropertyId = property.Id,
                    ResourceId = resource.Id,
                    Value = serializedDbValue,
                    Priority = contextMark.Priority
                }
            ]);
        }

        // Sync an unrelated mark. The four migrated marks stay Synced and therefore enter
        // the run only through ContextOldEffectsByMarkId -- the beta.144 corruption path.
        var triggerProperty = await AddProperty("Partial-sync trigger", PropertyType.SingleLineText);
        await AddPropertyMark(triggerProperty.Id, "touch");
        await SyncPendingPropertyMarks();

        var refreshedSingleChoice = await propertyService.GetByKey(singleChoice.Id);
        var refreshedMultipleChoice = await propertyService.GetByKey(multipleChoice.Id);
        var refreshedTags = await propertyService.GetByKey(tags.Id);
        var refreshedMultilevel = await propertyService.GetByKey(multilevel.Id);

        var singleOptions = refreshedSingleChoice.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject;
        singleOptions.Choices.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ChoiceOptions {Label = "Action", Value = singleChoiceId});

        var multipleOptions = refreshedMultipleChoice.Options.Should()
            .BeOfType<MultipleChoicePropertyOptions>().Subject;
        multipleOptions.Choices.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ChoiceOptions {Label = "Comedy", Value = multipleChoiceId});

        var tagOptions = refreshedTags.Options.Should().BeOfType<TagsPropertyOptions>().Subject;
        tagOptions.Tags.Should().ContainSingle(t =>
            t.Group == "Genre" && t.Name == "Drama" && t.Value == tagId);

        var multilevelOptions = refreshedMultilevel.Options.Should()
            .BeOfType<MultilevelPropertyOptions>().Subject;
        var multilevelRoot = multilevelOptions.Data.Should().ContainSingle().Subject;
        multilevelRoot.Label.Should().Be("Region");
        multilevelRoot.Value.Should().Be(multilevelRootId);
        var multilevelLeaf = multilevelRoot.Children.Should().ContainSingle().Subject;
        multilevelLeaf.Label.Should().Be("Japan");
        multilevelLeaf.Value.Should().Be(multilevelLeafId);

        foreach (var (property, serializedDbValue, canonicalEffectValue) in legacyCases)
        {
            var stored = (await propertyValueService.GetAllDbModels(v =>
                    v.ResourceId == resource.Id && v.PropertyId == property.Id))
                .Should().ContainSingle().Subject;
            stored.Value.Should().Be(serializedDbValue,
                "a V220 DB value must not be converted as if its UUID were a business label");

            var normalizedEffect = (await effectService.GetPropertyEffectsByMarkId(contextMarkIds[property.Id]))
                .Should().ContainSingle().Subject;
            normalizedEffect.Value.Should().Be(canonicalEffectValue,
                "legacy DB-valued effects should be rewritten to the canonical serialized business value");
        }

        // Filtering operates on DB ids. This catches the user-visible half of the regression:
        // every original human-labelled option must still select the id stored on the resource.
        await RebuildSearchIndex();
        await AssertReferenceSearchMatches(resource.Id, refreshedSingleChoice,
            SearchOperation.Equals, singleChoiceId);
        await AssertReferenceSearchMatches(resource.Id, refreshedMultipleChoice,
            SearchOperation.Contains, new List<string> {multipleChoiceId});
        await AssertReferenceSearchMatches(resource.Id, refreshedTags,
            SearchOperation.Contains, new List<string> {tagId});
        await AssertReferenceSearchMatches(resource.Id, refreshedMultilevel,
            SearchOperation.Contains, new List<string> {multilevelLeafId});
    }

    [TestMethod]
    public async Task PartialSync_Beta144PollutedSingleChoice_RepairsValueAndSearchWithoutDeletingChoices()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        var resourcePath = Path.Combine(_testRoot, "Movie");
        Directory.CreateDirectory(resourcePath);
        await resourceService.AddOrPutRange([new Resource {Path = resourcePath, IsFile = false}]);
        var resource = (await resourceService.GetAll()).Should().ContainSingle().Subject;

        // beta.144 interpreted the V220 effect's old DB id (U0) as a business label,
        // creating U1 and moving the synchronized property value to it.
        var originalChoiceId = Guid.NewGuid().ToString();
        var pollutedChoiceId = Guid.NewGuid().ToString();
        var property = await AddProperty(
            "Polluted single choice",
            PropertyType.SingleChoice,
            new SingleChoicePropertyOptions
            {
                Choices =
                [
                    new ChoiceOptions {Label = "Action", Value = originalChoiceId},
                    new ChoiceOptions {Label = originalChoiceId, Value = pollutedChoiceId}
                ]
            });

        var contextMark = await AddPropertyMark(property.Id, "Action");
        await pathMarkService.MarkAsSynced(contextMark.Id);
        await propertyValueService.AddDbModelRange([
            new CustomPropertyValueDbModel
            {
                ResourceId = resource.Id,
                PropertyId = property.Id,
                Scope = (int) PropertyValueScope.Synchronization,
                Value = pollutedChoiceId
            }
        ]);
        await effectService.AddPropertyEffects([
            new PropertyMarkEffect
            {
                MarkId = contextMark.Id,
                PropertyPool = PropertyPool.Custom,
                PropertyId = property.Id,
                ResourceId = resource.Id,
                Value = originalChoiceId,
                Priority = contextMark.Priority
            }
        ]);

        var triggerProperty = await AddProperty("Partial-sync trigger", PropertyType.SingleLineText);
        await AddPropertyMark(triggerProperty.Id, "touch");

        // The index must contain the beta.144 state before partial sync starts. Rebuilding
        // afterwards would hide the incremental-update consistency window reported by users.
        await RebuildSearchIndex();
        await AssertSingleChoiceInSearchMatches(resource.Id, property.Id, pollutedChoiceId);
        await AssertSingleChoiceInSearchDoesNotMatch(property.Id, originalChoiceId);

        await SyncPendingPropertyMarks();

        // This is intentionally the first operation after SyncMarks returns: no delay,
        // polling, or full rebuild may mask an update that is still queued or in flight.
        await AssertSingleChoiceInSearchMatches(resource.Id, property.Id, originalChoiceId);
        await AssertSingleChoiceInSearchDoesNotMatch(property.Id, pollutedChoiceId);

        var refreshedProperty = await propertyService.GetByKey(property.Id);
        var refreshedOptions = refreshedProperty.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject;
        refreshedOptions.Choices.Should().HaveCount(2,
            "path-mark sync cannot safely delete an orphan option that may have been user-created");
        refreshedOptions.Choices.Should().Contain(c =>
            c.Label == "Action" && c.Value == originalChoiceId);
        refreshedOptions.Choices.Should().Contain(c =>
            c.Label == originalChoiceId && c.Value == pollutedChoiceId);

        var stored = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == property.Id))
            .Should().ContainSingle().Subject;
        stored.Value.Should().Be(originalChoiceId,
            "the polluted U1 reference must be repaired to the original human-labelled option U0");

        var normalizedEffect = (await effectService.GetPropertyEffectsByMarkId(contextMark.Id))
            .Should().ContainSingle().Subject;
        normalizedEffect.Value.Should().Be("Action",
            "the V220 DB-valued effect should be persisted in canonical business-value form");

        var reloadedResource = await resourceService.Get(resource.Id, ResourceAdditionalItem.Properties);
        var reloadedValue = reloadedResource!.Properties![(int) PropertyPool.Custom][property.Id]
            .Values!.Should().ContainSingle(v => v.Scope == (int) PropertyValueScope.Synchronization).Subject;
        reloadedValue.Value.Should().Be(originalChoiceId);
        reloadedValue.BizValue.Should().Be("Action");
    }

    [TestMethod]
    public async Task Resync_PropertyMarkWithoutMatches_RemovesValueFromReadyIndexBeforeSyncReturns()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();

        var resourcePath = Path.Combine(_testRoot, "Movie");
        Directory.CreateDirectory(resourcePath);
        await resourceService.AddOrPutRange([new Resource {Path = resourcePath, IsFile = false}]);
        var resource = (await resourceService.GetAll()).Should().ContainSingle().Subject;

        var property = await AddProperty("Deleted property mark", PropertyType.SingleChoice);
        var mark = await AddPropertyMark(property.Id, "Action");
        await SyncPendingPropertyMarks();

        var refreshedProperty = await propertyService.GetByKey(property.Id);
        var choiceId = refreshedProperty.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject.Choices.Should()
            .ContainSingle(c => c.Label == "Action").Which.Value;

        await RebuildSearchIndex();
        await AssertSingleChoiceInSearchMatches(resource.Id, property.Id, choiceId);

        mark.Path = Path.Combine(_testRoot, "NoLongerMatches");
        await pathMarkService.Update(mark);
        await SyncPendingPropertyMarks();

        // The resource still exists, but its removed synchronization value must not remain
        // searchable after the successful re-sync reports completion.
        await AssertSingleChoiceInSearchDoesNotMatch(property.Id, choiceId);
        (await resourceService.GetAll()).Should().ContainSingle(r => r.Id == resource.Id);
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == property.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().BeEmpty();
    }

    [TestMethod]
    public async Task Resync_SameEffectKey_UpdatesThePersistedEffectValue()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        var resourcePath = Path.Combine(_testRoot, "Movie");
        Directory.CreateDirectory(resourcePath);
        await resourceService.AddOrPutRange([new Resource {Path = resourcePath, IsFile = false}]);
        var resource = (await resourceService.GetAll()).Should().ContainSingle().Subject;

        var property = await AddProperty("Mutable mark value", PropertyType.SingleChoice);
        var mark = await AddPropertyMark(property.Id, "Before");

        await SyncPendingPropertyMarks();

        var initialEffect = (await effectService.GetPropertyEffectsByMarkId(mark.Id))
            .Should().ContainSingle().Subject;
        initialEffect.Value.Should().Be("Before");

        var markToUpdate = (await pathMarkService.GetAll()).Single(m => m.Id == mark.Id);
        markToUpdate.ConfigJson = BuildPropertyMarkConfig(property.Id, "After");
        markToUpdate.Priority = 75;
        await pathMarkService.Update(markToUpdate);

        await SyncPendingPropertyMarks();

        var updatedEffect = (await effectService.GetPropertyEffectsByMarkId(mark.Id))
            .Should().ContainSingle("the same effect key must be updated, not left stale or duplicated")
            .Subject;
        updatedEffect.Value.Should().Be("After");
        updatedEffect.Priority.Should().Be(75,
            "priority is part of the effect data and must be refreshed together with its value");

        var refreshedProperty = await propertyService.GetByKey(property.Id);
        var options = refreshedProperty.Options.Should().BeOfType<SingleChoicePropertyOptions>().Subject;
        var afterChoiceId = options.Choices.Should().ContainSingle(c => c.Label == "After").Which.Value;

        var stored = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == property.Id))
            .Should().ContainSingle().Subject;
        stored.Value.Should().Be(afterChoiceId);

        var typedDbValue = stored.Value!.DeserializeAsStandardValue(
            PropertySystem.Property.GetDbValueType(PropertyType.SingleChoice));
        PropertySystem.Property.ToBizValue(refreshedProperty.ToProperty(), typedDbValue)
            .Should().Be("After");
    }

    [TestMethod]
    public async Task PartialSync_GuidShapedBusinessLabel_RemainsALabel_WhenReExtractionWorksOrIsAmbiguous()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        var resourcePath = Path.Combine(_testRoot, "Movie");
        Directory.CreateDirectory(resourcePath);
        await resourceService.AddOrPutRange([new Resource {Path = resourcePath, IsFile = false}]);
        var resource = (await resourceService.GetAll()).Should().ContainSingle().Subject;

        // A GUID is a perfectly valid user-facing label.  Legacy-value detection must not
        // classify it as a DB reference merely because it has the same lexical shape.
        var guidLabel = Guid.NewGuid().ToString();
        var property = await AddProperty("GUID-shaped label", PropertyType.SingleChoice);
        var mark = await AddPropertyMark(property.Id, guidLabel);

        await SyncPendingPropertyMarks();

        var initiallyRefreshedProperty = await propertyService.GetByKey(property.Id);
        var initialOptions = initiallyRefreshedProperty.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject;
        var choice = initialOptions.Choices.Should()
            .ContainSingle(c => c.Label == guidLabel).Which;
        choice.Value.Should().NotBe(guidLabel,
            "the GUID-shaped text is the business label, not a caller-supplied DB id");

        var initialEffect = (await effectService.GetPropertyEffectsByMarkId(mark.Id))
            .Should().ContainSingle().Subject;
        initialEffect.Value.Should().Be(guidLabel);

        // The mark is now context-only.  An unrelated sync exercises the compatibility
        // classifier without recollecting this fixed value as a pending mark.
        var triggerProperty = await AddProperty("Partial-sync trigger", PropertyType.SingleLineText);
        await AddPropertyMark(triggerProperty.Id, "touch");
        await SyncPendingPropertyMarks();

        var finallyRefreshedProperty = await propertyService.GetByKey(property.Id);
        var finalOptions = finallyRefreshedProperty.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject;
        finalOptions.Choices.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(choice);

        var finalEffect = (await effectService.GetPropertyEffectsByMarkId(mark.Id))
            .Should().ContainSingle().Subject;
        finalEffect.Value.Should().Be(guidLabel);

        var stored = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == property.Id))
            .Should().ContainSingle().Subject;
        stored.Value.Should().Be(choice.Value);

        var reloadedResource = await resourceService.Get(resource.Id, ResourceAdditionalItem.Properties);
        var reloadedValue = reloadedResource!.Properties![(int) PropertyPool.Custom][property.Id]
            .Values!.Should().ContainSingle(v => v.Scope == (int) PropertyValueScope.Synchronization).Subject;
        reloadedValue.BizValue.Should().Be(guidLabel);

        // Now remove the independent path/config evidence and create the exact lexical
        // ambiguity that a DB -> Biz -> DB round-trip cannot solve: the legitimate label
        // is also another choice's id. A later partial sync must preserve, not guess.
        var ambiguousProperty = await propertyService.GetByKey(property.Id);
        var ambiguousOptions = ambiguousProperty.Options.Should()
            .BeOfType<SingleChoicePropertyOptions>().Subject;
        ambiguousOptions.Choices!.Add(new ChoiceOptions {Label = "Decoy", Value = guidLabel});
        await propertyService.Put(ambiguousProperty);

        // Simulate already-persisted corruption directly; normal saves reject invalid JSON.
        var markOrm = _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, PathMarkDbModel, int>>();
        var ambiguousMark = (await markOrm.GetByKey(mark.Id))!;
        ambiguousMark.ConfigJson = "{";
        await markOrm.Update(ambiguousMark);
        await pathMarkService.MarkAsSynced(mark.Id);

        var secondTriggerProperty = await AddProperty("Second partial-sync trigger", PropertyType.SingleLineText);
        await AddPropertyMark(secondTriggerProperty.Id, "touch again");
        await SyncPendingPropertyMarks();

        var preservedEffect = (await effectService.GetPropertyEffectsByMarkId(mark.Id))
            .Should().ContainSingle().Subject;
        preservedEffect.Value.Should().Be(guidLabel);

        var preservedValue = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == property.Id))
            .Should().ContainSingle().Subject;
        preservedValue.Value.Should().Be(choice.Value,
            "an ambiguous business label must not be reinterpreted as the decoy choice id");

        var preservedResource = await resourceService.Get(resource.Id, ResourceAdditionalItem.Properties);
        var preservedResourceValue = preservedResource!.Properties![(int) PropertyPool.Custom][property.Id]
            .Values!.Should().ContainSingle(v => v.Scope == (int) PropertyValueScope.Synchronization).Subject;
        preservedResourceValue.BizValue.Should().Be(guidLabel);
    }

    [TestMethod]
    public async Task Sync_V220SelectorlessPropertyMarks_ApplyToChildResource_AndAreImmediatelySearchable()
    {
        var avPath = Path.Combine(_testRoot, "TBD", "AV");
        var resourcePath = Path.Combine(avPath, "Work-001");
        Directory.CreateDirectory(resourcePath);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();

        var statusProperty = await AddProperty("Status", PropertyType.SingleChoice);
        var textProperty = await AddProperty("Status text", PropertyType.SingleLineText);
        var legacyRegexProperty = await AddProperty("Legacy resource code", PropertyType.SingleLineText);
        var migratedRegexProperty = await AddProperty("Migrated resource code", PropertyType.SingleLineText);
        var modernRegexProperty = await AddProperty("Modern mark code", PropertyType.SingleLineText);

        await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 0
        });

        // This is the exact shape emitted by V220: the applicability selector has
        // matchMode=Layer, but both layer and regex are absent. ValueLayer=-1 means
        // the parent of AV (TBD) is the dynamic business value.
        var statusMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = BuildV220SelectorlessPropertyConfig(statusProperty.Id),
            Priority = 100
        });
        var textMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = BuildV220SelectorlessPropertyConfig(textProperty.Id),
            Priority = 100
        });
        var regexMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = BuildV220SelectorlessRegexPropertyConfig(legacyRegexProperty.Id, @"^(Work)-(?:\d+)$"),
            Priority = 100
        });
        var migratedRegexMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = migratedRegexProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ValueRegex = @"^(Work)-(?:\d+)$",
                ValueRegexMatchesResourcePath = true,
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
            }),
            Priority = 100
        });
        var modernRegexMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = modernRegexProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ValueRegex = "^(AV)$",
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
            }),
            Priority = 100
        });

        await SyncPendingResources();

        var resource = (await resourceService.GetAll()).Should()
            .ContainSingle(r => r.Path == resourcePath).Subject;
        resource.Status.Should().Be(ResourceStatus.Active);

        // Keep the index ready before property sync. Assertions below intentionally run
        // immediately after SyncMarks returns, without a rebuild, delay, or polling.
        await RebuildSearchIndex();
        await SyncPendingPropertyMarks();

        var statusEffect = (await effectService.GetPropertyEffectsByMarkId(statusMark.Id))
            .Should().ContainSingle().Subject;
        statusEffect.ResourceId.Should().Be(resource.Id);
        statusEffect.Value.Should().Be("TBD",
            "path-mark effects store the extracted business label, not a reference id");

        var textEffect = (await effectService.GetPropertyEffectsByMarkId(textMark.Id))
            .Should().ContainSingle().Subject;
        textEffect.ResourceId.Should().Be(resource.Id);
        textEffect.Value.Should().Be("TBD",
            "selector compatibility must apply before property-type conversion");

        var regexEffect = (await effectService.GetPropertyEffectsByMarkId(regexMark.Id))
            .Should().ContainSingle().Subject;
        regexEffect.ResourceId.Should().Be(resource.Id);
        regexEffect.Value.Should().Be("Work",
            "a V220 regex locator extracts each resource's path relative to the old media-library root");
        (await effectService.GetPropertyEffectsByMarkId(migratedRegexMark.Id))
            .Should().ContainSingle().Which.Value.Should().Be("Work",
                "the corrected V220 representation must retain resource-relative regex semantics");
        (await effectService.GetPropertyEffectsByMarkId(modernRegexMark.Id))
            .Should().ContainSingle().Which.Value.Should().Be("AV",
                "modern property regex extraction remains relative to the mark directory");

        var refreshedStatus = await propertyService.GetByKey(statusProperty.Id);
        var choice = refreshedStatus.Options.Should().BeOfType<SingleChoicePropertyOptions>()
            .Subject.Choices.Should().ContainSingle().Subject;
        choice.Label.Should().Be("TBD");
        Guid.TryParse(choice.Value, out _).Should().BeTrue("reference values are stored as generated UUIDs");
        choice.Value.Should().NotBe(choice.Label);

        var statusValue = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == statusProperty.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().ContainSingle().Subject;
        statusValue.Value.Should().Be(choice.Value);

        var textValue = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == textProperty.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().ContainSingle().Subject;
        textValue.Value.Should().Be("TBD");

        var regexValue = (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == legacyRegexProperty.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().ContainSingle().Subject;
        regexValue.Value.Should().Be("Work");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == migratedRegexProperty.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().ContainSingle().Which.Value.Should().Be("Work");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id &&
                v.PropertyId == modernRegexProperty.Id &&
                v.Scope == (int) PropertyValueScope.Synchronization))
            .Should().ContainSingle().Which.Value.Should().Be("AV");

        await AssertSingleChoiceInSearchMatches(resource.Id, statusProperty.Id, choice.Value);

        var countResponse = await ActivatorUtilities.CreateInstance<PropertyController>(_sp)
            .GetValueResourceCounts(PropertyPool.Custom, statusProperty.Id, new ResourceSearchInputModel());
        countResponse.Data.Should().NotBeNull();
        countResponse.Data!.IsReady.Should().BeTrue();
        countResponse.Data.Counts.Should().ContainKey(choice.Value);
        countResponse.Data.Counts[choice.Value].Should().Be(1);
    }

    [TestMethod]
    public async Task Sync_LegacyPropertyFallback_DoesNotExpandExplicitPropertyOrMediaLibrarySelectors()
    {
        var avPath = Path.Combine(_testRoot, "TBD", "AV");
        var resourcePath = Path.Combine(avPath, "Work-001");
        Directory.CreateDirectory(resourcePath);

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        var effectService = _sp.GetRequiredService<IPathMarkEffectService>();
        var mediaLibraryService = _sp.GetRequiredService<IMediaLibraryV2Service>();
        var mappingService = _sp.GetRequiredService<IMediaLibraryResourceMappingService>();

        var explicitProperty = await AddProperty("Explicit current-layer property", PropertyType.SingleLineText);
        var fixedSelectorlessProperty = await AddProperty("Malformed fixed property", PropertyType.SingleLineText);
        var dynamicSelectorlessProperty = await AddProperty(
            "Malformed dynamic property without extractor", PropertyType.SingleLineText);
        var explicitMissingExtractorProperty = await AddProperty(
            "Explicit dynamic property without extractor", PropertyType.SingleLineText);
        var migratedRegexProperty = await AddProperty("Migrated regex property", PropertyType.SingleLineText);
        var currentDirectoryLibrary = await mediaLibraryService.Add(
            new MediaLibraryV2AddOrPutInputModel("Current directory only", []));
        var childDirectoryLibrary = await mediaLibraryService.Add(
            new MediaLibraryV2AddOrPutInputModel("Direct children", []));
        var selectorlessLibrary = await mediaLibraryService.Add(
            new MediaLibraryV2AddOrPutInputModel("Malformed selectorless media library", []));

        await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 0
        });

        var explicitPropertyMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = explicitProperty.Id,
                ValueType = PropertyValueType.Fixed,
                FixedValue = "must-not-reach-child",
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 100
        });
        var fixedSelectorlessPropertyMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = BuildSelectorlessFixedPropertyConfig(
                fixedSelectorlessProperty.Id, "must-not-use-legacy-fallback"),
            Priority = 100
        });
        var dynamicSelectorlessPropertyMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = BuildSelectorlessDynamicPropertyWithoutExtractorConfig(dynamicSelectorlessProperty.Id),
            Priority = 100
        });
        var explicitMissingExtractorMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = explicitMissingExtractorProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
            }),
            Priority = 100
        });
        var migratedRegexMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                Pool = PropertyPool.Custom,
                PropertyId = migratedRegexProperty.Id,
                ValueType = PropertyValueType.Dynamic,
                ValueRegex = @"^(Work)",
                ValueRegexMatchesResourcePath = true,
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
            }),
            Priority = 100
        });
        var currentDirectoryMediaMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 0,
                ValueType = PropertyValueType.Fixed,
                MediaLibraryId = currentDirectoryLibrary.Id,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 100
        });
        var childDirectoryMediaMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                ValueType = PropertyValueType.Fixed,
                MediaLibraryId = childDirectoryLibrary.Id,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 100
        });
        var selectorlessMediaMark = await pathMarkService.Add(new PathMark
        {
            Path = avPath,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = BuildSelectorlessFixedMediaLibraryConfig(selectorlessLibrary.Id),
            Priority = 100
        });

        await SyncPendingResources();
        await SyncPendingPropertyMarks();

        var resource = (await resourceService.GetAll()).Should()
            .ContainSingle(r => r.Path == resourcePath).Subject;
        (await effectService.GetPropertyEffectsByMarkId(migratedRegexMark.Id))
            .Should().ContainSingle().Which.Value.Should().Be("Work");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == migratedRegexProperty.Id))
            .Should().ContainSingle().Which.Value.Should().Be("Work");

        var markWithClearedRegex = (await pathMarkService.Get(migratedRegexMark.Id))!;
        markWithClearedRegex.ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 0,
            Pool = PropertyPool.Custom,
            PropertyId = migratedRegexProperty.Id,
            ValueType = PropertyValueType.Dynamic,
            ValueLayer = -1,
            ValueRegex = string.Empty,
            ValueRegexMatchesResourcePath = true,
            ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories
        });
        await pathMarkService.Update(markWithClearedRegex);
        await SyncPendingPropertyMarks();

        (await effectService.GetPropertyEffectsByMarkId(explicitPropertyMark.Id)).Should().BeEmpty(
            "an explicit layer=0 + MatchedOnly property mark must not expand to child resources");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == explicitProperty.Id))
            .Should().BeEmpty();
        (await effectService.GetPropertyEffectsByMarkId(fixedSelectorlessPropertyMark.Id)).Should().BeEmpty(
            "only V220 dynamic locators qualify for selectorless compatibility");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == fixedSelectorlessProperty.Id))
            .Should().BeEmpty();
        (await effectService.GetPropertyEffectsByMarkId(dynamicSelectorlessPropertyMark.Id)).Should().BeEmpty(
            "a dynamic mark without a value layer or value regex is malformed, not a V220 locator");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == dynamicSelectorlessProperty.Id))
            .Should().BeEmpty();
        (await effectService.GetPropertyEffectsByMarkId(explicitMissingExtractorMark.Id)).Should().BeEmpty(
            "a missing dynamic extractor must not silently fall back to layer zero");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == explicitMissingExtractorProperty.Id))
            .Should().BeEmpty();
        (await effectService.GetPropertyEffectsByMarkId(migratedRegexMark.Id)).Should().BeEmpty(
            "a cleared migrated regex must not silently fall back to its stale value layer");
        (await propertyValueService.GetAllDbModels(v =>
                v.ResourceId == resource.Id && v.PropertyId == migratedRegexProperty.Id))
            .Should().BeEmpty();

        (await effectService.GetPropertyEffectsByMarkId(currentDirectoryMediaMark.Id)).Should().BeEmpty(
            "legacy compatibility is property-specific and must not change media-library selectors");
        (await effectService.GetPropertyEffectsByMarkId(selectorlessMediaMark.Id)).Should().BeEmpty(
            "a selectorless media-library mark is malformed, not a V220 property locator");
        var childMediaEffect = (await effectService.GetPropertyEffectsByMarkId(childDirectoryMediaMark.Id))
            .Should().ContainSingle().Subject;
        childMediaEffect.ResourceId.Should().Be(resource.Id);
        childMediaEffect.Value.Should().Be(childDirectoryLibrary.Id.ToString());

        var mediaLibraryIds = await mappingService.GetMediaLibraryIdsByResourceId(resource.Id);
        mediaLibraryIds.Should().BeEquivalentTo([childDirectoryLibrary.Id]);
    }

    [TestMethod]
    public void LegacyWildcardApplicability_RequiresTheV220MatchModeAndExtractorPair()
    {
        var baseConfig = new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Pool = PropertyPool.Custom,
            PropertyId = 1,
            ValueType = PropertyValueType.Dynamic,
            ValueLayer = -1,
            ApplyScope = PathMarkApplyScope.MatchedOnly
        };

        baseConfig.UsesLegacyV220WildcardApplicability().Should().BeTrue();

        new PropertyMarkConfig
        {
            MatchMode = (PathMatchMode) 0,
            ValueType = PropertyValueType.Dynamic,
            ValueLayer = -1,
            ApplyScope = PathMarkApplyScope.MatchedOnly
        }.UsesLegacyV220WildcardApplicability().Should().BeFalse();

        new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Regex,
            ValueType = PropertyValueType.Dynamic,
            ValueLayer = -1,
            ApplyScope = PathMarkApplyScope.MatchedOnly
        }.UsesLegacyV220WildcardApplicability().Should().BeFalse();

        new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            ValueType = PropertyValueType.Dynamic,
            ValueRegex = "^(AV)$",
            ApplyScope = PathMarkApplyScope.MatchedOnly
        }.UsesLegacyV220WildcardApplicability().Should().BeFalse();
    }

    private async Task<CustomProperty> AddProperty(string name, PropertyType type, object? options = null)
    {
        return await _sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        {
            Name = name,
            Type = type,
            Options = options == null ? null : JsonConvert.SerializeObject(options)
        });
    }

    private async Task<PathMark> AddPropertyMark(int propertyId, string fixedValue)
    {
        return await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = BuildPropertyMarkConfig(propertyId, fixedValue),
            Priority = 50
        });
    }

    private async Task<PathMark> AddLayerPropertyMark(int propertyId)
    {
        return await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                Pool = PropertyPool.Custom,
                PropertyId = propertyId,
                ValueType = PropertyValueType.Dynamic,
                ValueLayer = 1,
                ApplyScope = PathMarkApplyScope.MatchedOnly
            }),
            Priority = 50
        });
    }

    private string BuildPropertyMarkConfig(int propertyId, string fixedValue)
    {
        return JsonConvert.SerializeObject(new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 1,
            Pool = PropertyPool.Custom,
            PropertyId = propertyId,
            ValueType = PropertyValueType.Fixed,
            FixedValue = fixedValue,
            ApplyScope = PathMarkApplyScope.MatchedOnly
        });
    }

    private static string BuildV220SelectorlessPropertyConfig(int propertyId)
    {
        return $"{{\"matchMode\":1,\"pool\":4,\"propertyId\":{propertyId},\"valueType\":2," +
               "\"valueLayer\":-1,\"applyScope\":1}";
    }

    private static string BuildV220SelectorlessRegexPropertyConfig(int propertyId, string valueRegex)
    {
        return $"{{\"matchMode\":2,\"pool\":4,\"propertyId\":{propertyId},\"valueType\":2," +
               $"\"valueRegex\":{JsonConvert.SerializeObject(valueRegex)},\"applyScope\":1}}";
    }

    private static string BuildSelectorlessFixedPropertyConfig(int propertyId, string fixedValue)
    {
        return $"{{\"matchMode\":1,\"pool\":4,\"propertyId\":{propertyId},\"valueType\":1," +
               $"\"fixedValue\":{JsonConvert.SerializeObject(fixedValue)},\"applyScope\":1}}";
    }

    private static string BuildSelectorlessDynamicPropertyWithoutExtractorConfig(int propertyId)
    {
        return $"{{\"matchMode\":1,\"pool\":4,\"propertyId\":{propertyId},\"valueType\":2," +
               "\"applyScope\":1}";
    }

    private static string BuildSelectorlessFixedMediaLibraryConfig(int mediaLibraryId)
    {
        return $"{{\"matchMode\":1,\"valueType\":1,\"mediaLibraryId\":{mediaLibraryId},\"applyScope\":1}}";
    }

    private async Task SyncPendingResources()
    {
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark,
            null,
            null,
            new PauseToken(),
            CancellationToken.None);
    }

    private async Task SyncPendingPropertyMarks()
    {
        await _sp.GetRequiredService<PathMarkSyncService>().SyncMarks(
            null,
            null,
            new PauseToken(),
            CancellationToken.None);
    }

    private async Task RebuildSearchIndex()
    {
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<ResourceSearch> BuildSingleChoiceInApiSearch(int propertyId, params string[] choiceIds)
    {
        var serializedList = choiceIds.ToList().SerializeAsStandardValue(StandardValueType.ListString);
        serializedList.Should().NotBeNull();

        var input = new ResourceSearchInputModel
        {
            Page = 1,
            PageSize = 100,
            Group = new ResourceSearchFilterGroupInputModel
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilterInputModel
                    {
                        PropertyPool = PropertyPool.Custom,
                        PropertyId = propertyId,
                        Operation = SearchOperation.In,
                        DbValue = serializedList
                    }
                ]
            }
        };

        return await input.ToDomainModel(_sp.GetRequiredService<IPropertyService>());
    }

    private async Task AssertSingleChoiceInSearchMatches(
        int expectedResourceId,
        int propertyId,
        string choiceId)
    {
        var search = await BuildSingleChoiceInApiSearch(propertyId, choiceId);
        var indexedIds = await _sp.GetRequiredService<IResourceSearchIndexService>()
            .SearchResourceIdsAsync(search.Group);
        indexedIds.Should().NotBeNull(
            "the ready inverted index must answer the filter instead of allowing a full-scan fallback");
        indexedIds!.Should().ContainSingle(id => id == expectedResourceId);

        var result = await _sp.GetRequiredService<IResourceService>().Search(search);
        result.Data.Should().ContainSingle(r => r.Id == expectedResourceId);
    }

    private async Task AssertSingleChoiceInSearchDoesNotMatch(int propertyId, string choiceId)
    {
        var search = await BuildSingleChoiceInApiSearch(propertyId, choiceId);
        var indexedIds = await _sp.GetRequiredService<IResourceSearchIndexService>()
            .SearchResourceIdsAsync(search.Group);
        indexedIds.Should().NotBeNull(
            "the ready inverted index must answer the filter instead of allowing a full-scan fallback");
        indexedIds!.Should().BeEmpty();

        var result = await _sp.GetRequiredService<IResourceService>().Search(search);
        result.Data.Should().NotBeNull();
        result.Data!.Count.Should().Be(0);
    }

    private async Task AssertReferenceSearchMatches(
        int expectedResourceId,
        CustomProperty property,
        SearchOperation operation,
        object dbValue)
    {
        var result = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch
        {
            PageSize = 100,
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilter
                    {
                        PropertyPool = PropertyPool.Custom,
                        PropertyId = property.Id,
                        Operation = operation,
                        DbValue = dbValue,
                        Property = property.ToProperty()
                    }
                ]
            }
        });
        result.Data.Should().ContainSingle(r => r.Id == expectedResourceId);
    }
}
