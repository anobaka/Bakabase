using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for ExtensionGroupService — a previously untested service:
/// add / get / get-all / add-range / put / delete.
/// </summary>
[TestClass]
public sealed class ExtensionGroupServiceTests
{
    private IServiceProvider _sp = null!;
    private IExtensionGroupService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IExtensionGroupService>();
    }

    [TestMethod]
    public async Task GetAll_Empty_WhenNothingAdded()
        => Assert.AreEqual(0, (await _service.GetAll()).Length);

    [TestMethod]
    public async Task Add_ThenGetAll_ReturnsGroup()
    {
        await _service.Add(new ExtensionGroupAddInputModel("images", [".jpg", ".png"]));
        var all = await _service.GetAll();
        Assert.AreEqual(1, all.Length);
        Assert.AreEqual("images", all[0].Name);
        Assert.AreEqual(2, all[0].Extensions!.Count);
    }

    [TestMethod]
    public async Task Get_ReturnsGroupById()
    {
        var added = await _service.Add(new ExtensionGroupAddInputModel("videos", [".mp4"]));
        var fetched = await _service.Get(added.Id);
        Assert.AreEqual("videos", fetched.Name);
    }

    [TestMethod]
    public async Task AddRange_AddsEveryGroup()
    {
        await _service.AddRange(
        [
            new ExtensionGroupAddInputModel("a", [".a"]),
            new ExtensionGroupAddInputModel("b", [".b"]),
        ]);
        Assert.AreEqual(2, (await _service.GetAll()).Length);
    }

    [TestMethod]
    public async Task Put_UpdatesNameAndExtensions()
    {
        var added = await _service.Add(new ExtensionGroupAddInputModel("before", [".old"]));
        await _service.Put(added.Id, new ExtensionGroupPutInputModel("after", [".new1", ".new2"]));

        var fetched = await _service.Get(added.Id);
        Assert.AreEqual("after", fetched.Name);
        Assert.AreEqual(2, fetched.Extensions!.Count);
    }

    [TestMethod]
    public async Task Delete_RemovesGroup()
    {
        var added = await _service.Add(new ExtensionGroupAddInputModel("doomed", [".x"]));
        await _service.Delete(added.Id);
        Assert.AreEqual(0, (await _service.GetAll()).Length);
    }
}
