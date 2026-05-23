using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Pins the invariant: every entry point that accepts a user-supplied path must run it
/// through StandardizePath on storage. A non-standardized value stored in the DB causes
/// path comparisons (sync, filtering, conflict detection) to silently miss.
/// </summary>
[TestClass]
public sealed class EntryPointPathStandardizationTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup() => _sp = await TestServiceBuilder.BuildServiceProvider();

    private const string QuirkPath = "/foo//bar/";
    private const string Expected = "/foo/bar";

    [TestMethod]
    public async Task PathMarkService_Add_StandardizesPath()
    {
        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        var added = await pathMarkService.Add(new PathMark
        {
            Path = QuirkPath,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });
        Assert.AreEqual(Expected, added.Path);
    }

    [TestMethod]
    public async Task MediaLibraryV2Service_Add_StandardizesEveryPath()
    {
        var service = _sp.GetRequiredService<IMediaLibraryV2Service>();
        var added = await service.Add(new MediaLibraryV2AddOrPutInputModel(
            "X", [QuirkPath, "/baz//qux/"]));

        var fetched = await service.Get(added.Id);
        CollectionAssert.AreEqual(new List<string> { "/baz/qux", Expected }, fetched!.Paths);
    }

    [TestMethod]
    public async Task MediaLibraryV2Service_Put_StandardizesEveryPath()
    {
        var service = _sp.GetRequiredService<IMediaLibraryV2Service>();
        var added = await service.Add(new MediaLibraryV2AddOrPutInputModel("X", ["/initial"]));
        await service.Put(added.Id, new MediaLibraryV2AddOrPutInputModel("X", [QuirkPath]));

        var fetched = await service.Get(added.Id);
        CollectionAssert.AreEqual(new List<string> { Expected }, fetched!.Paths);
    }
}
