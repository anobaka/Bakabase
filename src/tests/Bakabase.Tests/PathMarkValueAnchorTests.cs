using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public sealed class PathMarkValueAnchorTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private string _markPath = null!;
    private string _first = null!;
    private string _second = null!;
    private List<ResourceDbModel> _resources = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"PathMarkAnchors.{Guid.NewGuid():N}");
        _markPath = Path.Combine(_root, "Anime");
        _first = Path.Combine(_markPath, "作者甲", "已阅", "a.mp4");
        _second = Path.Combine(_markPath, "作者乙", "2026", "未阅览", "b.mkv");
        foreach (var path in new[] { _first, _second })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, string.Empty);
        }
        _resources = await _sp.GetRequiredService<IResourceService>().AddAll(new[]
        {
            new ResourceDbModel { Path = Normalize(_first), IsFile = true },
            new ResourceDbModel { Path = Normalize(_second), IsFile = true },
            new ResourceDbModel { Path = Normalize(_markPath) },
            new ResourceDbModel { Path = Normalize(_root) }
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task FixedMediaLibraryPreview_ShowsCurrentNameAndNoValueForDeletedOrMissingLibrary()
    {
        var libraries = _sp.GetRequiredService<IMediaLibraryV2Service>();
        var library = await libraries.Add(new MediaLibraryV2AddOrPutInputModel("Original library", []));
        var request = new PathMarkPreviewRequest
        {
            Path = _markPath, Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer, Layer = 1,
                ValueType = PropertyValueType.Fixed, MediaLibraryId = library.Id
            })
        };
        var originalConfig = request.ConfigJson;
        var marks = _sp.GetRequiredService<IPathMarkService>();
        var initial = await marks.PreviewMatchedPaths(request);
        initial.Should().HaveCount(2).And.OnlyContain(result => result.PropertyValue == "Original library");

        await libraries.Put(library.Id, new MediaLibraryV2AddOrPutInputModel("Renamed library", []));
        var renamed = await marks.PreviewMatchedPaths(request);
        renamed.Should().HaveCount(2).And.OnlyContain(result => result.PropertyValue == "Renamed library");

        await libraries.Delete(library.Id);
        var deleted = await marks.PreviewMatchedPaths(request);
        deleted.Should().HaveCount(2).And.OnlyContain(result => result.PropertyValue == null);
        request.ConfigJson.Should().Be(originalConfig);

        request.ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 1,
            ValueType = PropertyValueType.Fixed, MediaLibraryId = int.MaxValue
        });
        var missing = await marks.PreviewMatchedPaths(request);
        missing.Should().HaveCount(2).And.OnlyContain(result => result.PropertyValue == null);
        (await marks.GetAll()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task DynamicMediaLibraryPreview_PreservesNumericNameThatMatchesAnotherLibraryId()
    {
        var libraries = _sp.GetRequiredService<IMediaLibraryV2Service>();
        var library = await libraries.Add(new MediaLibraryV2AddOrPutInputModel("Unrelated library", []));
        var numericName = library.Id.ToString();
        var numericPath = Path.Combine(_markPath, numericName);
        Directory.CreateDirectory(numericPath);
        var preview = await _sp.GetRequiredService<IPathMarkService>().PreviewMatchedPaths(new PathMarkPreviewRequest
        {
            Path = numericPath, Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer, Layer = 0,
                ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
                ValueBasePath = PathMarkValueBasePath.MarkPath, LayerToMediaLibrary = 0
            })
        });

        preview.Should().ContainSingle().Which.PropertyValue.Should().Be(numericName);
        (await libraries.GetAll()).Should().ContainSingle().Which.Name.Should().Be("Unrelated library");
    }

    [TestMethod]
    [DataRow(PropertyType.SingleChoice, PropertyValueType.Fixed)]
    [DataRow(PropertyType.MultipleChoice, PropertyValueType.Fixed)]
    [DataRow(PropertyType.Tags, PropertyValueType.Fixed)]
    [DataRow(PropertyType.Multilevel, PropertyValueType.Fixed)]
    [DataRow(PropertyType.SingleChoice, PropertyValueType.Dynamic)]
    [DataRow(PropertyType.MultipleChoice, PropertyValueType.Dynamic)]
    [DataRow(PropertyType.Tags, PropertyValueType.Dynamic)]
    [DataRow(PropertyType.Multilevel, PropertyValueType.Dynamic)]
    public async Task ReferencePropertyPreview_PreservesBusinessLabelsWithoutResolvingIdsOrMutatingOptions(
        PropertyType type, PropertyValueType valueType)
    {
        var guidLabel = Guid.NewGuid().ToString();
        object options = type switch
        {
            PropertyType.SingleChoice => new SingleChoicePropertyOptions
                {Choices = [new ChoiceOptions {Value = guidLabel, Label = "Decoy"}]},
            PropertyType.MultipleChoice => new MultipleChoicePropertyOptions
                {Choices = [new ChoiceOptions {Value = guidLabel, Label = "Decoy"}]},
            PropertyType.Tags => new TagsPropertyOptions
                {Tags = [new TagsPropertyOptions.TagOptions("Decoy", "Old tag") {Value = guidLabel}]},
            _ => new MultilevelPropertyOptions
                {Data = [new MultilevelDataOptions {Value = guidLabel, Label = "Decoy"}]}
        };
        object businessValue = type switch
        {
            PropertyType.SingleChoice => guidLabel,
            PropertyType.MultipleChoice => new List<string> {guidLabel, "New label"},
            PropertyType.Tags => new List<TagValue> {new("Group", guidLabel), new(null, "New tag")},
            _ => new List<List<string>> {new() {"Root", guidLabel}, new() {"New root", "Leaf"}}
        };
        var serialized = businessValue.SerializeAsStandardValue(type.GetBizValueType());
        var property = await AddProperty(type, options);
        var properties = _sp.GetRequiredService<ICustomPropertyService>();
        var optionsBefore = JsonConvert.SerializeObject((await properties.GetByKey(property.Id)).Options);
        var previewPath = Path.Combine(_markPath, serialized!);
        Directory.CreateDirectory(previewPath);
        var request = new PathMarkPreviewRequest
        {
            Path = previewPath, Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                Pool = PropertyPool.Custom, PropertyId = property.Id,
                MatchMode = PathMatchMode.Layer, Layer = 0,
                ValueType = valueType, FixedValue = valueType == PropertyValueType.Fixed ? serialized : null,
                ValueBasePath = PathMarkValueBasePath.MarkPath, ValueMatchMode = PathMatchMode.Layer, ValueLayer = 0
            })
        };
        var preview = await _sp.GetRequiredService<IPathMarkService>().PreviewMatchedPaths(request);

        preview.Should().ContainSingle().Which.PropertyValue.Should().Be(serialized);
        JsonConvert.SerializeObject((await properties.GetByKey(property.Id)).Options).Should().Be(optionsBefore);
        (await _sp.GetRequiredService<ICustomPropertyValueService>()
            .GetAllDbModels(value => value.PropertyId == property.Id)).Should().BeEmpty();
        (await _sp.GetRequiredService<IPathMarkEffectService>()
            .GetPropertyEffectsByResources(_resources.Select(resource => resource.Id))).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(PathMarkValueBasePath.MarkPath, 0)]
    [DataRow(PathMarkValueBasePath.MarkPath, -1)]
    [DataRow(PathMarkValueBasePath.MarkPath, 1)]
    [DataRow(PathMarkValueBasePath.MarkPath, 2)]
    [DataRow(PathMarkValueBasePath.ResourcePath, 0)]
    [DataRow(PathMarkValueBasePath.ResourcePath, -1)]
    [DataRow(PathMarkValueBasePath.ResourcePath, -2)]
    public async Task LayerAnchors_PreviewEffectsAndPrecreatedLibrariesAgree(PathMarkValueBasePath anchor, int layer)
    {
        var sources = (anchor, layer) switch
        {
            (PathMarkValueBasePath.MarkPath, 0) => new[] { _markPath, _markPath },
            (PathMarkValueBasePath.MarkPath, -1) => new[] { _root, _root },
            (PathMarkValueBasePath.MarkPath, 1) => new[]
            {
                Path.Combine(_markPath, "作者甲"), Path.Combine(_markPath, "作者乙")
            },
            (PathMarkValueBasePath.MarkPath, 2) => new[]
            {
                Path.Combine(_markPath, "作者甲", "已阅"), Path.Combine(_markPath, "作者乙", "2026")
            },
            (PathMarkValueBasePath.ResourcePath, 0) => new[] { _first, _second },
            (PathMarkValueBasePath.ResourcePath, -1) => new[]
            {
                Path.GetDirectoryName(_first)!, Path.GetDirectoryName(_second)!
            },
            _ => new[] { Path.GetDirectoryName(Path.GetDirectoryName(_first))!, Path.GetDirectoryName(Path.GetDirectoryName(_second))! }
        };
        var property = await AddProperty();
        await AssertBothMarks(Configs(property.Id, anchor, PathMatchMode.Layer, layer),
            sources.Select(Path.GetFileName).ToArray()!, sources);
    }

    [TestMethod]
    [DataRow(PathMarkValueBasePath.MarkPath)]
    [DataRow(PathMarkValueBasePath.ResourcePath)]
    public async Task RegexAnchors_AreIndependentOfApplicability(PathMarkValueBasePath anchor)
    {
        var property = await AddProperty();
        var regex = anchor == PathMarkValueBasePath.MarkPath ? "^(Ani)me$" : @"^(.+)\.[^.]+$";
        await AssertBothMarks(Configs(property.Id, anchor, PathMatchMode.Regex, null, regex),
            anchor == PathMarkValueBasePath.MarkPath ? new[] { "Ani", "Ani" } : new[] { "a", "b" },
            anchor == PathMarkValueBasePath.MarkPath ? new[] { _markPath, _markPath } : new[] { _first, _second });
    }

    [TestMethod]
    public async Task MediaLibrary_ExplicitLayerExtractionWorksWithRegexApplicability()
    {
        var config = new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Regex, Regex = "^作者甲$",
            ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
            ValueBasePath = PathMarkValueBasePath.ResourcePath, LayerToMediaLibrary = -1
        };
        await AssertRegexScope(config, new[] { "已阅" }, new[] { _first });
    }

    [TestMethod]
    public async Task MissingAnchorAndModeFields_PreserveMarkRelativeDefaults()
    {
        var property = await AddProperty();
        var configs = Configs(property.Id, PathMarkValueBasePath.MarkPath, PathMatchMode.Layer, 0)
            .Select(c =>
            {
                var json = JObject.Parse(c.ConfigJson);
                json.Remove("ValueBasePath"); json.Remove("ValueMatchMode");
                return (c.Type, ConfigJson: json.ToString());
            }).ToArray();
        await AssertBothMarks(configs, new[] { "Anime", "Anime" }, new[] { _markPath, _markPath });
    }

    [TestMethod]
    public async Task MissingExtractionMode_RetainsDifferentHistoricalPropertyAndLibrarySelectors()
    {
        var property = await AddProperty();
        var configs = new[]
        {
            (PathMarkType.Property, JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                Pool = PropertyPool.Custom, PropertyId = property.Id,
                MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                ValueType = PropertyValueType.Dynamic, ValueLayer = 0, ValueRegex = "^(Ani)me$"
            })),
            (PathMarkType.MediaLibrary, JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Regex, Regex = ".+", ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                ValueType = PropertyValueType.Dynamic, LayerToMediaLibrary = 0, RegexToMediaLibrary = "^(Ani)me$"
            }))
        };
        await AssertBothMarks(configs, new[] { "Ani", "Ani" }, new[] { _markPath, _markPath });
    }

    [TestMethod]
    public async Task ResourceRegexWithoutMatches_DoesNotPrecreateLibrariesOrEffects()
    {
        var property = await AddProperty();
        var marks = new List<PathMark>();
        foreach (var config in Configs(property.Id, PathMarkValueBasePath.ResourcePath, PathMatchMode.Regex, null, "^(missing)$"))
        {
            marks.Add(await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
            { Path = _markPath, Type = config.Type, ConfigJson = config.ConfigJson }));
        }
        await Sync();
        foreach (var mark in marks)
            (await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id)).Should().BeEmpty();
        (await _sp.GetRequiredService<IMediaLibraryV2Service>().GetAll()).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(PathMarkType.Property)]
    [DataRow(PathMarkType.MediaLibrary)]
    public async Task ModernInvalidExtractors_AreRejectedBeforeSaveAndPreview(PathMarkType type)
    {
        var service = _sp.GetRequiredService<IPathMarkService>();
        var configs = new[]
        {
            new { ValueBasePath = 2, ValueMatchMode = 1, ValueLayer = (int?)1, ValueRegex = (string?)null },
            new { ValueBasePath = 99, ValueMatchMode = 1, ValueLayer = (int?)0, ValueRegex = (string?)null },
            new { ValueBasePath = 1, ValueMatchMode = 99, ValueLayer = (int?)0, ValueRegex = (string?)null },
            new { ValueBasePath = 1, ValueMatchMode = 1, ValueLayer = (int?)null, ValueRegex = (string?)null },
            new { ValueBasePath = 1, ValueMatchMode = 2, ValueLayer = (int?)null, ValueRegex = (string?)null },
            new { ValueBasePath = 1, ValueMatchMode = 2, ValueLayer = (int?)null, ValueRegex = (string?)"[" }
        };
        foreach (var config in configs)
        {
            var json = JsonConvert.SerializeObject(new
            {
                config.ValueBasePath, config.ValueMatchMode,
                config.ValueLayer, LayerToMediaLibrary = config.ValueLayer,
                config.ValueRegex, RegexToMediaLibrary = config.ValueRegex,
                ValueType = PropertyValueType.Dynamic,
                MatchMode = PathMatchMode.Layer, Layer = 1
            });
            Func<Task> save = () => service.Add(new PathMark { Path = _markPath, Type = type, ConfigJson = json });
            await save.Should().ThrowAsync<ArgumentException>();
            Func<Task> preview = () => service.PreviewMatchedPaths(new PathMarkPreviewRequest
            {
                Path = _markPath, Type = type, ConfigJson = json
            });
            await preview.Should().ThrowAsync<ArgumentException>();
        }
        (await service.GetAll()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ResourceAnchor_IsNotMistakenForSelectorlessV220()
    {
        var config = new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Regex, ValueRegex = "(Anime)",
            ValueType = PropertyValueType.Dynamic, ValueBasePath = PathMarkValueBasePath.ResourcePath
        };
        config.UsesLegacyV220WildcardApplicability().Should().BeFalse();
        config.UsesResourceRelativeValueRegex().Should().BeFalse();
    }

    [TestMethod]
    [DataRow(PathMarkApplyScope.MatchedAndSubdirectories, "^作者甲$", 1)]
    [DataRow(PathMarkApplyScope.MatchedOnly, "^作者甲$", 0)]
    [DataRow(PathMarkApplyScope.MatchedAndSubdirectories, "^$", 3)]
    public async Task RegexScope_InheritsFromUnmanagedAncestorsWithoutAddingExtraEffects(
        PathMarkApplyScope scope, string pattern, int expectedCount)
    {
        var config = new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Regex, Regex = pattern, ApplyScope = scope,
            ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
            ValueBasePath = PathMarkValueBasePath.ResourcePath, LayerToMediaLibrary = 0
        };
        var mark = await AddMark(PathMarkType.MediaLibrary, config);
        await Sync();
        var effects = await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id);
        effects.Should().HaveCount(expectedCount);
        effects.Select(e => e.ResourceId).Should().OnlyHaveUniqueItems();
        if (expectedCount == 1) effects[0].ResourceId.Should().Be(_resources[0].Id);
    }

    [TestMethod]
    public async Task ResourceRelativeChoiceEffects_AreReextractedDuringPartialSync()
    {
        var property = await AddProperty(PropertyType.SingleChoice);
        var config = new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = property.Id,
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
            ValueBasePath = PathMarkValueBasePath.ResourcePath, ValueLayer = 0
        };
        var mark = await AddMark(PathMarkType.Property, config);
        await Sync();
        var effectsService = _sp.GetRequiredService<IPathMarkEffectService>();
        var effects = await effectsService.GetPropertyEffectsByMarkId(mark.Id);
        var options = ((await _sp.GetRequiredService<ICustomPropertyService>().GetByKey(property.Id))
            .Options as SingleChoicePropertyOptions)!;
        foreach (var effect in effects) effect.Value = options.Choices!.Single(c => c.Label == effect.Value).Value;
        await effectsService.UpdatePropertyEffects(effects);
        await AddTrigger();
        await Sync();
        (await effectsService.GetPropertyEffectsByMarkId(mark.Id)).Select(e => e.Value)
            .Should().BeEquivalentTo(new[] { "a.mp4", "b.mkv" });
        ((await _sp.GetRequiredService<ICustomPropertyService>().GetByKey(property.Id)).Options
            as SingleChoicePropertyOptions)!.Choices.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task MissingLegacyLayer_DoesNotTurnIntoMarkNameDuringPartialReferenceSync()
    {
        var property = await AddProperty(PropertyType.SingleChoice);
        var config = new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = property.Id,
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Fixed, FixedValue = "Original"
        };
        var mark = await AddMark(PathMarkType.Property, config);
        await Sync();
        config.ValueType = PropertyValueType.Dynamic;
        config.FixedValue = null;
        mark.ConfigJson = JsonConvert.SerializeObject(config);
        var marks = _sp.GetRequiredService<IPathMarkService>();
        await marks.Update(mark);
        await marks.MarkAsSynced(mark.Id);
        await AddTrigger();
        await Sync();
        (await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id))
            .Select(e => e.Value).Should().HaveCount(2).And.OnlyContain(v => v == "Original");
        var choices = ((await _sp.GetRequiredService<ICustomPropertyService>().GetByKey(property.Id))
            .Options as SingleChoicePropertyOptions)!.Choices;
        choices.Should().ContainSingle().Which.Label.Should().Be("Original");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacyFullPathRegex_KeepsAllCapturesAndTypeConversion(bool explicitCompatibilityFlag)
    {
        var property = await AddProperty(PropertyType.MultipleChoice);
        var config = new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = property.Id,
            ValueType = PropertyValueType.Dynamic,
            ValueRegex = @"^(作者[^/]+)/(?:[^/]+/)*([^/]+)\.[^.]+$",
            MatchMode = explicitCompatibilityFlag ? PathMatchMode.Layer : PathMatchMode.Regex,
            Layer = explicitCompatibilityFlag ? 1 : null,
            ApplyScope = explicitCompatibilityFlag
                ? PathMarkApplyScope.MatchedAndSubdirectories : PathMarkApplyScope.MatchedOnly,
            ValueRegexMatchesResourcePath = explicitCompatibilityFlag,
            ValueBasePath = explicitCompatibilityFlag ? PathMarkValueBasePath.ResourcePath : PathMarkValueBasePath.MarkPath,
            ValueMatchMode = explicitCompatibilityFlag ? PathMatchMode.Regex : null
        };
        var mark = await AddMark(PathMarkType.Property, config);
        var preview = await _sp.GetRequiredService<IPathMarkService>().PreviewMatchedPaths(new PathMarkPreviewRequest
        { Path = _markPath, Type = mark.Type, ConfigJson = mark.ConfigJson });
        await Sync();
        var effects = await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id);
        effects.Should().HaveCount(2);
        for (var i = 0; i < 2; i++)
        {
            var expected = i == 0 ? new[] { "作者甲", "a" } : new[] { "作者乙", "b" };
            var effect = effects.Single(e => e.ResourceId == _resources[i].Id);
            effect.Value!.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString)
                .Should().BeEquivalentTo(expected);
            var sample = preview.Single(p => p.Path == _resources[i].Path);
            sample.PropertyValue!.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString)
                .Should().BeEquivalentTo(expected);
            sample.ValueSourcePath.Should().Be(_resources[i].Path);
        }
    }

    [TestMethod]
    public async Task OldRegexConfigurations_PreservePropertyFailureAndMediaLibraryFallback()
    {
        var property = await AddProperty();
        var propertyMark = await AddMark(PathMarkType.Property, new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = property.Id,
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueRegex = "["
        });
        var mediaLibraryMark = await AddMark(PathMarkType.MediaLibrary, new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Regex, Regex = ".+", ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, RegexToMediaLibrary = "[", LayerToMediaLibrary = 0
        });
        var result = await _sp.GetRequiredService<PathMarkSyncService>()
            .SyncMarks(null, null, new PauseToken(), CancellationToken.None);
        result.FailedMarks.Should().Be(1);
        (await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(propertyMark.Id))
            .Should().BeEmpty();
        (await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mediaLibraryMark.Id))
            .Should().HaveCount(2);
        (await _sp.GetRequiredService<IMediaLibraryV2Service>().GetAll()).Should().ContainSingle()
            .Which.Name.Should().Be("Anime");
    }

    [TestMethod]
    public async Task Preview_IsBoundedCancellableAndRespectsMountedSymlinks()
    {
        var blocked = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(blocked);
        await File.WriteAllTextAsync(Path.Combine(blocked, "secret.mp4"), string.Empty);
        if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(_markPath, "escape"), blocked);
        // A lexical test policy intentionally does not resolve links: preview must check their target too.
        var storage = new MountedTestPolicy(_markPath);
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<IUserStoragePolicy>(); services.AddSingleton<IUserStoragePolicy>(storage);
        });
        for (var i = 0; i < 80; i++) await File.WriteAllTextAsync(Path.Combine(_markPath, $"sample{i}.mp4"), string.Empty);
        var request = new PathMarkPreviewRequest
        {
            Path = _markPath, Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
                ValueBasePath = PathMarkValueBasePath.ResourcePath, LayerToMediaLibrary = 0
            })
        };
        var service = _sp.GetRequiredService<IPathMarkService>();
        var preview = await service.PreviewMatchedPaths(request);
        preview.Should().HaveCountLessThanOrEqualTo(50);
        preview.Should().NotContain(p => p.Path.Contains("escape") || p.ValueSourcePath!.Contains("blocked"));
        storage.Calls.Should().BeLessThan(1000, "bounded previews must not scan the entire tree");
        request.ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueMatchMode = PathMatchMode.Layer,
            ValueBasePath = PathMarkValueBasePath.MarkPath, LayerToMediaLibrary = -1
        });
        var ancestors = await service.PreviewMatchedPaths(request);
        ancestors.Should().OnlyContain(p => p.ValueSourcePath == null,
            "an ancestor value does not authorize exposing its unmounted source path");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> cancel = () => service.PreviewMatchedPaths(request, cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        request.Path = blocked;
        Func<Task> forbidden = () => service.PreviewMatchedPaths(request);
        await forbidden.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private async Task AssertBothMarks((PathMarkType Type, string ConfigJson)[] configs, string[] expected, string[] sources)
    {
        var marks = new List<PathMark>();
        var service = _sp.GetRequiredService<IPathMarkService>();
        foreach (var config in configs)
        {
            var preview = await service.PreviewMatchedPaths(new PathMarkPreviewRequest
            { Path = _markPath, Type = config.Type, ConfigJson = config.ConfigJson });
            foreach (var (path, index) in new[] { _first, _second }.Select((p, i) => (p, i)))
            {
                var sample = preview.FirstOrDefault(p => p.Path == Normalize(path))
                    ?? preview.FirstOrDefault(p => p.Path == Normalize(sources[index]))
                    ?? preview.First();
                sample.PropertyValue.Should().Be(expected[index]);
                sample.ValueSourcePath.Should().Be(Normalize(sources[index]));
                if ((int?)JObject.Parse(config.ConfigJson)["ValueBasePath"] == (int)PathMarkValueBasePath.ResourcePath)
                    preview.Take(10).Should().Contain(p => p.Path == Normalize(path));
            }
            marks.Add(await service.Add(new PathMark
            { Path = _markPath, Type = config.Type, ConfigJson = config.ConfigJson }));
        }
        await Sync();
        var libraries = (await _sp.GetRequiredService<IMediaLibraryV2Service>().GetAll()).ToDictionary(l => l.Id, l => l.Name);
        libraries.Values.Should().BeEquivalentTo(expected.Distinct());
        foreach (var mark in marks)
        {
            var effects = await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id);
            effects.Should().HaveCount(2, "each mark contributes once for each matching existing resource");
            for (var i = 0; i < 2; i++)
            {
                var effect = effects.Should().ContainSingle(e => e.ResourceId == _resources[i].Id).Subject;
                var actual = mark.Type == PathMarkType.Property ? effect.Value : libraries[int.Parse(effect.Value!)];
                actual.Should().Be(expected[i]);
            }
        }
        var mappings = await _sp.GetRequiredService<IMediaLibraryResourceMappingService>().GetAll();
        mappings.Should().HaveCount(2);
        (await _sp.GetRequiredService<IResourceService>().GetAll()).Should().HaveCount(_resources.Count);
    }

    private async Task AssertRegexScope(MediaLibraryMarkConfig config, string[] expected, string[] paths)
    {
        var preview = await _sp.GetRequiredService<IPathMarkService>().PreviewMatchedPaths(new PathMarkPreviewRequest
        { Path = _markPath, Type = PathMarkType.MediaLibrary, ConfigJson = JsonConvert.SerializeObject(config) });
        for (var i = 0; i < paths.Length; i++)
            preview.Should().ContainSingle(p => p.Path == Normalize(paths[i])).Subject.PropertyValue.Should().Be(expected[i]);
        var mark = await AddMark(PathMarkType.MediaLibrary, config);
        await Sync();
        var effects = await _sp.GetRequiredService<IPathMarkEffectService>().GetPropertyEffectsByMarkId(mark.Id);
        effects.Should().HaveCount(paths.Length);
        (await _sp.GetRequiredService<IMediaLibraryV2Service>().GetAll()).Select(l => l.Name).Should().BeEquivalentTo(expected);
    }

    private static (PathMarkType Type, string ConfigJson)[] Configs(int propertyId, PathMarkValueBasePath anchor,
        PathMatchMode valueMode, int? valueLayer, string? valueRegex = null) =>
    [
        (PathMarkType.Property, JsonConvert.SerializeObject(new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = propertyId,
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueBasePath = anchor,
            ValueMatchMode = valueMode, ValueLayer = valueLayer, ValueRegex = valueRegex
        })),
        (PathMarkType.MediaLibrary, JsonConvert.SerializeObject(new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 1, ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Dynamic, ValueBasePath = anchor,
            ValueMatchMode = valueMode, LayerToMediaLibrary = valueLayer, RegexToMediaLibrary = valueRegex
        }))
    ];

    private Task<CustomProperty> AddProperty(PropertyType type = PropertyType.SingleLineText, object? options = null) =>
        _sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        { Name = "Anchor value", Type = type, Options = options == null ? null : JsonConvert.SerializeObject(options) });
    private Task<PathMark> AddMark(PathMarkType type, object config) => _sp.GetRequiredService<IPathMarkService>()
        .Add(new PathMark { Path = _markPath, Type = type, ConfigJson = JsonConvert.SerializeObject(config) });
    private async Task AddTrigger()
    {
        var property = await AddProperty();
        await AddMark(PathMarkType.Property, new PropertyMarkConfig
        {
            Pool = PropertyPool.Custom, PropertyId = property.Id, MatchMode = PathMatchMode.Layer, Layer = 1,
            ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
            ValueType = PropertyValueType.Fixed, FixedValue = "touch"
        });
    }
    private async Task Sync()
    {
        var result = await _sp.GetRequiredService<PathMarkSyncService>()
            .SyncMarks(null, null, new PauseToken(), CancellationToken.None);
        result.FailedMarks.Should().Be(0);
    }
    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed class MountedTestPolicy(string root) : IUserStoragePolicy
    {
        public int Calls { get; private set; }
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) =>
            new[] { new UserStorageRoot(root, "Mounted", "Local", false) };
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            Calls++;
            return Normalize(path) == Normalize(root) || Normalize(path).StartsWith(Normalize(root) + "/", StringComparison.Ordinal);
        }
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        { if (!IsPathAllowed(path, purpose)) throw new UnauthorizedAccessException(); }
        public void EnsureTreeMutationAllowed(string path) => EnsurePathAllowed(path);
    }
}
