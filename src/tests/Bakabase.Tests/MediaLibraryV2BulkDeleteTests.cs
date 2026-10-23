using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Bootstrap.Components.Orm;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests;

[TestClass]
public sealed class MediaLibraryV2BulkDeleteTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private IMediaLibraryV2Service Libraries => _sp.GetRequiredService<IMediaLibraryV2Service>();
    private IPathMarkService Marks => _sp.GetRequiredService<IPathMarkService>();
    private IMediaLibraryResourceMappingService Mappings => _sp.GetRequiredService<IMediaLibraryResourceMappingService>();
    private IPathMarkEffectService Effects => _sp.GetRequiredService<IPathMarkEffectService>();

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseTests_LibraryDelete_" + Guid.NewGuid().ToString("N"));
        _sp = await TestServiceBuilder.BuildServiceProvider(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_sp as IDisposable)?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task<int> AddLibrary(string name) =>
        (await Libraries.Add(new MediaLibraryV2AddOrPutInputModel(name, []))).Id;

    private async Task<int> AddResource() => (await _sp.GetRequiredService<IResourceService>().AddAll([
        new ResourceDbModel {Path = _root, IsFile = false}
    ])).Single().Id;

    private Task<PathMark> AddMark(MediaLibraryMarkConfig config) => Marks.Add(new PathMark
    {
        Path = _root, Type = PathMarkType.MediaLibrary, ConfigJson = JsonConvert.SerializeObject(config)
    });

    private static MediaLibraryMarkConfig Fixed(params int[] ids) => new()
    {
        MatchMode = PathMatchMode.Layer, Layer = 0, ValueType = PropertyValueType.Fixed,
        MediaLibraryId = ids[0], MediaLibraryIds = ids.ToList()
    };

    private async Task Sync() => await _sp.GetRequiredService<PathMarkSyncService>()
        .SyncMarks(null, null, new PauseToken(), CancellationToken.None);

    [TestMethod]
    public async Task BulkDelete_DefaultKeepsFixedMarksAndResources_WithoutRestoringDeletedMappings()
    {
        var a = await AddLibrary("Delete A");
        var b = await AddLibrary("Delete B");
        var resource = await AddResource();
        var mark = await AddMark(Fixed(a, b));
        await Sync();
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().BeEquivalentTo([a, b]);

        await Libraries.DeleteMany([a, b, a]);

        (await Libraries.GetAll()).Should().BeEmpty();
        (await Marks.Get(mark.Id))!.ConfigJson.Should().Be(mark.ConfigJson);
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().BeEmpty();
        (await Effects.GetPropertyEffectsByMarkId(mark.Id)).Should().BeEmpty();
        (await _sp.GetRequiredService<IResourceService>().GetByKeys([resource])).Should().ContainSingle();

        await Marks.MarkAsPending(mark.Id);
        await Sync();
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().BeEmpty(
            "a retained fixed mark cannot recreate references to a deleted library");
    }

    [TestMethod]
    public async Task BulkDelete_CleanupPreservesOtherFixedLibrariesAndTheirEffects_AcrossResync()
    {
        // Duplicate names still identify distinct libraries in a fixed mark.
        var a = await AddLibrary("Same name");
        var b = await AddLibrary("Same name");
        var keep = await AddLibrary("Keep");
        var resource = await AddResource();
        var mixed = await AddMark(Fixed(a, b, keep));
        var onlySelected = await AddMark(Fixed(a, b));
        var legacy = await AddMark(new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 0,
            ValueType = PropertyValueType.Fixed, MediaLibraryId = b
        });
        await Sync();
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().BeEquivalentTo([a, b, keep]);
        (await Effects.GetPropertyEffectsByMarkId(mixed.Id)).Should().ContainSingle(
            "a multi-library mark keeps one ledger row per resource");

        await Libraries.DeleteMany([a, b], removeRelatedPathMarks: true);

        (await Marks.Get(onlySelected.Id)).Should().BeNull();
        (await Marks.Get(legacy.Id)).Should().BeNull();
        var kept = (await Marks.Get(mixed.Id))!;
        JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(kept.ConfigJson)!
            .GetFixedMediaLibraryIds().Should().Equal(keep);
        (await Effects.GetPropertyEffectsByMarkId(mixed.Id)).Should().ContainSingle()
            .Which.Value.Should().Be(keep.ToString());
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().Equal(keep);

        await Sync();
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().Equal(keep);
        (await Effects.GetPropertyEffectsByMarkId(onlySelected.Id)).Should().BeEmpty();

        await Libraries.Delete(keep, removeRelatedPathMarks: true);
        (await Marks.Get(mixed.Id)).Should().BeNull();
        (await Effects.GetPropertyEffectsByMarkId(mixed.Id)).Should().BeEmpty();
        (await _sp.GetRequiredService<IResourceService>().GetByKeys([resource])).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Cleanup_LeavesDynamicAndUnrelatedPropertyMarksUnchanged()
    {
        var selected = await AddLibrary("Delete");
        var dynamicMark = await AddMark(new MediaLibraryMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 0, ValueType = PropertyValueType.Dynamic,
            MediaLibraryId = selected, MediaLibraryIds = [selected], LayerToMediaLibrary = 0
        });
        var customMark = await Marks.Add(new PathMark
        {
            Path = _root, Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                Pool = PropertyPool.Custom, PropertyId = (int) ResourceProperty.MediaLibraryV2Multi,
                ValueType = PropertyValueType.Fixed, FixedValue = new[] {selected.ToString()}
            })
        });
        var malformed = new PathMarkDbModel
        {
            Path = _root, Type = PathMarkType.MediaLibrary,
            ConfigJson = $"{{\"valueType\":1,\"mediaLibraryId\":{selected},\"valueBasePath\":999}}"
        };
        await _sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, PathMarkDbModel, int>>()
            .Add(malformed);

        await Libraries.DeleteMany([selected], true);

        (await Marks.Get(dynamicMark.Id))!.ConfigJson.Should().Be(dynamicMark.ConfigJson);
        (await Marks.Get(customMark.Id))!.ConfigJson.Should().Be(customMark.ConfigJson);
        (await Marks.Get(malformed.Id))!.ConfigJson.Should().Be(malformed.ConfigJson);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cleanup_HandlesHistoricalInternalPropertyMarksWithoutRemovingOtherFixedValues(bool serialized)
    {
        var selected = await AddLibrary("Delete");
        var keep = await AddLibrary("Keep");
        var mark = await Marks.Add(new PathMark
        {
            Path = _root, Type = PathMarkType.Property,
            ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
            {
                Pool = PropertyPool.Internal, PropertyId = (int) ResourceProperty.MediaLibraryV2Multi,
                ValueType = PropertyValueType.Fixed, FixedValue = serialized
                    ? JsonConvert.SerializeObject(new[] {selected.ToString(), keep.ToString()})
                    : new[] {selected.ToString(), keep.ToString()}
            })
        });
        await Libraries.DeleteMany([selected], true);
        var remaining = (await Marks.Get(mark.Id))!;
        var value = JObject.Parse(remaining.ConfigJson).GetValue("fixedValue", StringComparison.OrdinalIgnoreCase)!;
        value.Type.Should().Be(serialized ? JTokenType.String : JTokenType.Array);
        (serialized ? JArray.Parse(value.Value<string>()!) : value).Values<string>().Should().Equal(keep.ToString());
        await Libraries.DeleteMany([keep], true);
        (await Marks.Get(mark.Id)).Should().BeNull();
    }

    [TestMethod]
    public async Task Cleanup_CanRemoveFixedValuesWhenHistoricalPathIsNoLongerAllowed()
    {
        var selected = await AddLibrary("Delete");
        var keep = await AddLibrary("Keep");
        var mark = await AddMark(Fixed(selected, keep));
        (_sp as IDisposable)?.Dispose();
        _sp = await TestServiceBuilder.BuildServiceProvider(_root, services => services.AddSingleton<IUserStoragePolicy>(
            new UserStoragePolicy(isContainer: true, readMountInfo: () => string.Empty, appDataDirectory: () => _root)));
        _sp.GetRequiredService<IUserStoragePolicy>().IsPathAllowed(_root).Should().BeFalse();

        await Libraries.DeleteMany([selected], true);

        var remaining = (await Marks.Get(mark.Id))!;
        JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(remaining.ConfigJson)!
            .GetFixedMediaLibraryIds().Should().Equal(keep);
        remaining.Path.Should().Be(mark.Path);
    }

    [TestMethod]
    public async Task MultiLibraryMark_PreviewAndAdditionalItemsIncludeEveryCurrentLibrary()
    {
        var a = await AddLibrary("First");
        var b = await AddLibrary("Second");
        var mark = await AddMark(Fixed(a, b));
        var preview = await Marks.PreviewMatchedPaths(new PathMarkPreviewRequest
        {
            Path = _root, Type = mark.Type, ConfigJson = mark.ConfigJson
        });
        preview.Should().ContainSingle().Which.PropertyValue.Should().Be("First, Second");
        var populated = (await Marks.GetAll(additionalItems: PathMarkAdditionalItem.MediaLibrary)).Single();
        populated.MediaLibraries!.Select(l => l.Id).Should().Equal(a, b);
        populated.MediaLibrary!.Id.Should().Be(a);
    }

    [TestMethod]
    public async Task MultiLibraryMark_RejectsEmptyOrNonpositiveSelections_AndNeverFallsBackToLegacyId()
    {
        var id = await AddLibrary("Existing");
        foreach (var ids in new List<int>[] {[], [0], [-1]})
        {
            var config = Fixed(id);
            config.MediaLibraryIds = ids;
            Func<Task> add = async () => await AddMark(config);
            await add.Should().ThrowAsync<ArgumentException>();
        }
        (await Marks.GetAll()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task BulkDelete_RejectsInvalidIdsBeforeChangingAnyLibrary()
    {
        var id = await AddLibrary("Keep");
        Func<Task> delete = () => Libraries.DeleteMany([id, 0], true);
        await delete.Should().ThrowAsync<ArgumentException>();
        (await Libraries.Get(id)).Should().NotBeNull();
        await Libraries.DeleteMany([], true);
        (await Libraries.Get(id)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task BulkDelete_WaitsForRunningMarkSync_AndRemovesItsSnapshotMappings()
    {
        var id = await AddLibrary("Delete after sync");
        var resource = await AddResource();
        await AddMark(Fixed(id));
        var beforeMappingWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = _sp.GetRequiredService<PathMarkSyncService>().SyncMarks(async progress =>
        {
            if (progress != 65) return;
            beforeMappingWrite.SetResult();
            await resumeSync.Task;
        }, null, new PauseToken(), CancellationToken.None);
        await beforeMappingWrite.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var delete = Libraries.DeleteMany([id], true);
        try
        {
            await Task.Delay(100);
            delete.IsCompleted.Should().BeFalse("deletion must wait until the in-flight sync finishes its snapshot");
        }
        finally { resumeSync.TrySetResult(); }
        await Task.WhenAll(sync, delete).WaitAsync(TimeSpan.FromSeconds(10));

        (await Libraries.Get(id)).Should().BeNull();
        (await Mappings.GetMediaLibraryIdsByResourceId(resource)).Should().BeEmpty();
        (await Marks.GetAll()).Should().BeEmpty();
    }
}
