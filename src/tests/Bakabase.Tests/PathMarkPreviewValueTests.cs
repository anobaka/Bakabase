using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class PathMarkPreviewValueTests
{
    private IServiceProvider _sp = null!;
    private string _testRoot = null!;
    private string _markPath = null!;
    private List<ResourceDbModel> _resources = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(Path.GetTempPath(), $"PathMarkPreview.{Guid.NewGuid():N}");
        _markPath = Path.Combine(_testRoot, "精品_Collection");
        var firstBranch = Path.Combine(_markPath, "七彩直播");
        var secondBranch = Path.Combine(_markPath, "小红帽");
        Directory.CreateDirectory(firstBranch);
        Directory.CreateDirectory(secondBranch);
        var workPath = Path.Combine(firstBranch, "work.mp4");
        await File.WriteAllTextAsync(workPath, string.Empty);
        // A filesystem preview candidate does not become a resource just because it is shown.
        await File.WriteAllTextAsync(Path.Combine(_markPath, "unmanaged.mp4"), string.Empty);
        _resources = await _sp.GetRequiredService<IResourceService>().AddAll(new[]
        {
            new ResourceDbModel { Path = _markPath },
            new ResourceDbModel { Path = firstBranch },
            new ResourceDbModel { Path = secondBranch },
            new ResourceDbModel { Path = workPath, IsFile = true },
            new ResourceDbModel { Path = _testRoot }
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(-100)]
    [DataRow(100)]
    public async Task LayerValues_PreviewMatchesSyncedPropertyAndMediaLibrary(int? valueLayer)
    {
        string? Expected(string path)
        {
            return valueLayer switch
            {
                null or 0 => "精品_Collection",
                -1 => Path.GetFileName(_testRoot),
                1 => Path.GetRelativePath(_markPath, path)
                    .Split(Path.DirectorySeparatorChar)[0],
                2 when path.EndsWith("work.mp4", StringComparison.Ordinal) => "work.mp4",
                _ => null
            };
        }

        // The stable sync algorithm chooses extraction through MatchMode. A value regex
        // must remain ignored in layer mode, even when it does not match the mark name.
        await AssertPreviewAndSync(PathMatchMode.Layer, valueLayer, "^ignored$", Expected);
    }

    [TestMethod]
    [DataRow("^精品_(.+)$", "Collection")]
    [DataRow("Collection$", "Collection")]
    [DataRow("^精品_(collection)$", "Collection")]
    [DataRow("^(missing)$", null)]
    [DataRow("[", "精品_Collection")]
    [DataRow(null, null)]
    public async Task RegexValues_PreviewMatchesSyncedPropertyAndMediaLibrary(string? valueRegex, string? expected)
    {
        // A stored layer does not override regex extraction when MatchMode is Regex.
        // Invalid value regexes preserve the existing sync fallback to the mark name.
        await AssertPreviewAndSync(PathMatchMode.Regex, 0, valueRegex, _ => expected);
    }

    private async Task AssertPreviewAndSync(
        PathMatchMode matchMode,
        int? valueLayer,
        string? valueRegex,
        Func<string, string?> expectedValue)
    {
        var property = await _sp.GetRequiredService<ICustomPropertyService>().Add(new CustomPropertyAddOrPutDto
        {
            Name = "Preview value",
            Type = PropertyType.SingleLineText
        });
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var marks = new List<PathMark>();
        foreach (var type in new[] { PathMarkType.Property, PathMarkType.MediaLibrary })
        {
            object config = type == PathMarkType.Property
                ? new PropertyMarkConfig
                {
                    MatchMode = matchMode,
                    Layer = 1,
                    Regex = ".+",
                    ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                    Pool = PropertyPool.Custom,
                    PropertyId = property.Id,
                    ValueType = PropertyValueType.Dynamic,
                    ValueLayer = valueLayer,
                    ValueRegex = valueRegex
                }
                : new MediaLibraryMarkConfig
                {
                    MatchMode = matchMode,
                    Layer = 1,
                    Regex = ".+",
                    ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                    ValueType = PropertyValueType.Dynamic,
                    LayerToMediaLibrary = valueLayer,
                    RegexToMediaLibrary = valueRegex
                };
            var configJson = JsonConvert.SerializeObject(config);
            var preview = await pathMarkService.PreviewMatchedPaths(new PathMarkPreviewRequest
            {
                Path = _markPath,
                Type = type,
                ConfigJson = configJson
            });
            var expectedPaths = new List<string>
            {
                Path.Combine(_markPath, "七彩直播"),
                Path.Combine(_markPath, "小红帽"),
                Path.Combine(_markPath, "unmanaged.mp4")
            };
            if (matchMode == PathMatchMode.Regex || valueLayer == 2)
            {
                expectedPaths.Add(Path.Combine(_markPath, "七彩直播", "work.mp4"));
            }
            preview.Select(p => p.Path).Should().BeEquivalentTo(expectedPaths.Select(Normalize));
            if (valueLayer == 2 && matchMode == PathMatchMode.Layer)
            {
                preview[0].PropertyValue.Should().Be("work.mp4", "the client's short sample must show useful deep values");
            }
            foreach (var result in preview)
            {
                result.PropertyValue.Should().Be(expectedValue(result.Path),
                    $"{type} preview must use the same mark-relative value as synchronization");
            }
            marks.Add(await pathMarkService.Add(new PathMark
            {
                Path = _markPath,
                Type = type,
                ConfigJson = configJson
            }));
        }

        var syncResult = await _sp.GetRequiredService<PathMarkSyncService>()
            .SyncMarks(null, null, new PauseToken(), CancellationToken.None);
        syncResult.FailedMarks.Should().Be(0);
        var libraries = (await _sp.GetRequiredService<IMediaLibraryV2Service>().GetAll())
            .ToDictionary(l => l.Id, l => l.Name);
        foreach (var mark in marks)
        {
            var effects = await _sp.GetRequiredService<IPathMarkEffectService>()
                .GetPropertyEffectsByMarkId(mark.Id);
            foreach (var resource in _resources)
            {
                var applicable = Normalize(resource.Path!).StartsWith(Normalize(_markPath) + "/",
                    StringComparison.Ordinal);
                var expected = applicable ? expectedValue(resource.Path!) : null;
                var resourceEffects = effects.Where(e => e.ResourceId == resource.Id).ToList();
                if (expected == null)
                {
                    resourceEffects.Should().BeEmpty("the mark root, outside resources and missing values are excluded");
                }
                else
                {
                    var effect = resourceEffects.Should().ContainSingle(
                        "one mark contributes one value per existing resource, without ancestor expansion").Subject;
                    var actual = mark.Type == PathMarkType.Property
                        ? effect.Value
                        : libraries[int.Parse(effect.Value!)];
                    actual.Should().Be(expected);
                }
            }
        }
        var mediaLibraryMark = marks.Single(m => m.Type == PathMarkType.MediaLibrary);
        var mediaLibraryEffects = await _sp.GetRequiredService<IPathMarkEffectService>()
            .GetPropertyEffectsByMarkId(mediaLibraryMark.Id);
        var mappings = await _sp.GetRequiredService<IMediaLibraryResourceMappingService>().GetAll();
        mappings.Select(m => (m.ResourceId, m.MediaLibraryId)).Should().BeEquivalentTo(
            mediaLibraryEffects.Select(e => (e.ResourceId, int.Parse(e.Value!))));
        (await _sp.GetRequiredService<IResourceService>().GetAll()).Should().HaveCount(_resources.Count,
            "property and media-library marks must not create resources from preview paths");
    }

    [TestMethod]
    [DataRow(0, PropertyValueType.Dynamic)]
    [DataRow(2, PropertyValueType.Dynamic)]
    [DataRow(2, PropertyValueType.Fixed)]
    public async Task DescendantPreviewExpansion_LeavesResourceMarksAndMatchedOnlyUnchanged(
        int valueLayer, PropertyValueType valueType)
    {
        var service = _sp.GetRequiredService<IPathMarkService>();
        foreach (var type in new[] { PathMarkType.Resource, PathMarkType.Property, PathMarkType.MediaLibrary })
        {
            foreach (var scope in new[] { PathMarkApplyScope.MatchedOnly, PathMarkApplyScope.MatchedAndSubdirectories })
            {
                var preview = await service.PreviewMatchedPaths(new PathMarkPreviewRequest
                {
                    Path = _markPath,
                    Type = type,
                    ConfigJson = JsonConvert.SerializeObject(new
                    {
                        matchMode = PathMatchMode.Layer,
                        layer = 1,
                        applyScope = scope,
                        valueType,
                        fixedValue = "Fixed value",
                        mediaLibraryId = 1,
                        valueLayer,
                        layerToMediaLibrary = valueLayer
                    })
                });
                var expanded = valueType == PropertyValueType.Dynamic && valueLayer == 2 &&
                               type != PathMarkType.Resource &&
                               scope == PathMarkApplyScope.MatchedAndSubdirectories;
                preview.Select(p => p.Path).Should().Contain(Normalize(Path.Combine(_markPath, "七彩直播")));
                preview.Any(p => p.Path.EndsWith("work.mp4", StringComparison.Ordinal)).Should().Be(expanded);
                preview.Select(p => p.Path).Should().OnlyHaveUniqueItems();
            }
        }
    }

    [TestMethod]
    public async Task DeepValuePreview_IsBoundedAndDoesNotDescendPastTheValueLayer()
    {
        var branch = Path.Combine(_markPath, "七彩直播");
        for (var i = 0; i < 80; i++) Directory.CreateDirectory(Path.Combine(branch, $"item{i:D2}"));
        var belowTarget = Path.Combine(branch, "item00", "below-target.txt");
        await File.WriteAllTextAsync(belowTarget, string.Empty);
        var preview = await _sp.GetRequiredService<IPathMarkService>().PreviewMatchedPaths(new PathMarkPreviewRequest
        {
            Path = _markPath,
            Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                ApplyScope = PathMarkApplyScope.MatchedAndSubdirectories,
                ValueType = PropertyValueType.Dynamic,
                LayerToMediaLibrary = 2
            })
        });
        preview.Count(p => p.PropertyValue != null).Should().Be(50);
        preview.Select(p => p.Path).Should().NotContain(Normalize(belowTarget));
        preview[0].PropertyValue.Should().NotBeNull();
        preview.Select(p => p.Path).Should().Contain(Normalize(branch), "original matches are retained");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
