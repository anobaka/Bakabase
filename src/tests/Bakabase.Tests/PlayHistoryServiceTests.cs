using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for PlayHistoryService — a previously untested service:
/// add / get-all / filtered get-all / paged search.
/// </summary>
[TestClass]
public sealed class PlayHistoryServiceTests
{
    private IServiceProvider _sp = null!;
    private IPlayHistoryService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IPlayHistoryService>();
    }

    private Task Add(int resourceId, string? item = null)
        => _service.Add(new PlayHistoryDbModel
        {
            ResourceId = resourceId,
            Item = item,
            PlayedAt = DateTime.Now
        });

    [TestMethod]
    public async Task GetAll_Empty_WhenNothingAdded()
        => Assert.AreEqual(0, (await _service.GetAll()).Count);

    [TestMethod]
    public async Task Add_ThenGetAll_ReturnsEntry()
    {
        await Add(1, "/movie/a.mp4");
        var all = await _service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(1, all[0].ResourceId);
        Assert.AreEqual("/movie/a.mp4", all[0].Item);
    }

    [TestMethod]
    public async Task GetAll_FilterByResourceId()
    {
        await Add(1);
        await Add(2);
        await Add(2);
        Assert.AreEqual(2, (await _service.GetAll(x => x.ResourceId == 2)).Count);
    }

    [TestMethod]
    public async Task Search_ReturnsEveryEntry()
    {
        await Add(1);
        await Add(2);
        await Add(3);
        var response = await _service.Search();
        Assert.AreEqual(3, response.TotalCount);
    }

    [TestMethod]
    public async Task Search_Paginates()
    {
        for (var i = 1; i <= 5; i++)
        {
            await Add(i);
        }
        var response = await _service.Search(null, 0, 2);
        Assert.AreEqual(2, response.Data!.Count);
        Assert.AreEqual(5, response.TotalCount);
    }

    [TestMethod]
    public async Task Search_NoEntries_ReturnsEmpty()
    {
        var response = await _service.Search();
        Assert.AreEqual(0, response.TotalCount);
    }
}
