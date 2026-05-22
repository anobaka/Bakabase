using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for MediaLibraryV2Service CRUD — the existing tests only cover
/// delete cascade. Covers add / get / get-all / put / patch / delete.
/// </summary>
[TestClass]
public sealed class MediaLibraryV2ServiceTests
{
    private IServiceProvider _sp = null!;
    private IMediaLibraryV2Service _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IMediaLibraryV2Service>();
    }

    [TestMethod]
    public async Task Add_ThenGet_ReturnsLibrary()
    {
        var added = await _service.Add(new MediaLibraryV2AddOrPutInputModel("Movies", ["/movies"], "red"));
        var fetched = await _service.Get(added.Id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("Movies", fetched!.Name);
        Assert.AreEqual("red", fetched.Color);
    }

    [TestMethod]
    public async Task GetAll_ReturnsEveryLibrary()
    {
        await _service.Add(new MediaLibraryV2AddOrPutInputModel("A", ["/a"]));
        await _service.Add(new MediaLibraryV2AddOrPutInputModel("B", ["/b"]));
        Assert.AreEqual(2, (await _service.GetAll()).Count);
    }

    [TestMethod]
    public async Task Get_NonExistent_ReturnsNull()
        => Assert.IsNull(await _service.Get(999999));

    [TestMethod]
    public async Task Put_ReplacesFields()
    {
        var added = await _service.Add(new MediaLibraryV2AddOrPutInputModel("Before", ["/before"], "red"));
        await _service.Put(added.Id, new MediaLibraryV2AddOrPutInputModel("After", ["/after"], "blue"));

        var fetched = await _service.Get(added.Id);
        Assert.AreEqual("After", fetched!.Name);
        Assert.AreEqual("blue", fetched.Color);
    }

    [TestMethod]
    public async Task Patch_UpdatesOnlyProvidedFields()
    {
        var added = await _service.Add(new MediaLibraryV2AddOrPutInputModel("Original", ["/p"], "green"));
        await _service.Patch(added.Id, new MediaLibraryV2PatchInputModel { Name = "Renamed" });

        var fetched = await _service.Get(added.Id);
        Assert.AreEqual("Renamed", fetched!.Name);
        // Color was not part of the patch, so it must be left untouched.
        Assert.AreEqual("green", fetched.Color);
    }

    [TestMethod]
    public async Task Delete_RemovesLibrary()
    {
        var added = await _service.Add(new MediaLibraryV2AddOrPutInputModel("Doomed", ["/d"]));
        await _service.Delete(added.Id);
        Assert.IsNull(await _service.Get(added.Id));
    }
}
