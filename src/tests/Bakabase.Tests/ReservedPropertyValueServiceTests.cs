using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for ReservedPropertyValueService — a previously untested service
/// that stores reserved-property values (Rating, Introduction, CoverPaths, Name).
/// </summary>
[TestClass]
public sealed class ReservedPropertyValueServiceTests
{
    private IServiceProvider _sp = null!;
    private IReservedPropertyValueService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IReservedPropertyValueService>();
    }

    [TestMethod]
    public async Task GetAll_Empty_WhenNothingAdded()
        => Assert.AreEqual(0, (await _service.GetAll()).Count);

    [TestMethod]
    public async Task Add_ThenGetAll_ReturnsValue()
    {
        await _service.Add(new ReservedPropertyValue { ResourceId = 1, Scope = 1, Rating = 4.5m });
        var all = await _service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual(4.5m, all[0].Rating);
    }

    [TestMethod]
    public async Task GetFirst_ReturnsMatchingValue()
    {
        await _service.Add(new ReservedPropertyValue { ResourceId = 1, Scope = 1, Rating = 1m });
        await _service.Add(new ReservedPropertyValue { ResourceId = 2, Scope = 1, Rating = 2m });

        var found = await _service.GetFirst(x => x.ResourceId == 2);
        Assert.IsNotNull(found);
        Assert.AreEqual(2m, found!.Rating);
    }

    [TestMethod]
    public async Task Add_PersistsEveryReservedField()
    {
        await _service.Add(new ReservedPropertyValue
        {
            ResourceId = 7,
            Scope = 1,
            Rating = 3m,
            Introduction = "a description",
            CoverPaths = ["/covers/a.jpg"],
            Name = "Display Name"
        });

        var found = await _service.GetFirst(x => x.ResourceId == 7);
        Assert.IsNotNull(found);
        Assert.AreEqual(3m, found!.Rating);
        Assert.AreEqual("a description", found.Introduction);
        Assert.AreEqual("Display Name", found.Name);
        CollectionAssert.AreEqual(new List<string> { "/covers/a.jpg" }, found.CoverPaths);
    }

    [TestMethod]
    public async Task Update_ChangesStoredValue()
    {
        var added = (await _service.Add(
            new ReservedPropertyValue { ResourceId = 1, Scope = 1, Rating = 1m })).Data!;
        added.Rating = 9m;
        await _service.Update(added);

        var found = await _service.GetFirst(x => x.ResourceId == 1);
        Assert.AreEqual(9m, found!.Rating);
    }

    [TestMethod]
    public async Task AddRange_AddsEveryValue()
    {
        await _service.AddRange(
        [
            new ReservedPropertyValue { ResourceId = 1, Scope = 1, Rating = 1m },
            new ReservedPropertyValue { ResourceId = 2, Scope = 1, Rating = 2m },
        ]);
        Assert.AreEqual(2, (await _service.GetAll()).Count);
    }
}
