using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for SpecialTextService — a previously untested service: rule
/// CRUD, prefab population, name pretreatment (standardization + whitespace collapse),
/// and date-time parsing (configured format and the standard fallback).
/// </summary>
[TestClass]
public sealed class SpecialTextServiceTests
{
    private IServiceProvider _sp = null!;
    private ISpecialTextService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<ISpecialTextService>();
    }

    private Task Add(SpecialTextType type, string value1, string? value2 = null)
        => _service.Add(new SpecialTextAddInputModel { Type = type, Value1 = value1, Value2 = value2 });

    [TestMethod]
    public async Task Add_ThenGetAll_ReturnsTheRule()
    {
        await Add(SpecialTextType.Standardization, "from", "to");
        var all = await _service.GetAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("from", all[0].Value1);
        Assert.AreEqual("to", all[0].Value2);
    }

    [TestMethod]
    public async Task Count_ReflectsAddedRules()
    {
        await Add(SpecialTextType.Trim, "-");
        await Add(SpecialTextType.Useless, "junk");
        Assert.AreEqual(2, await _service.Count());
    }

    [TestMethod]
    public async Task AddPrefabs_PopulatesRules()
    {
        await _service.AddPrefabs();
        Assert.IsTrue(await _service.Count() > 0);
    }

    [TestMethod]
    public async Task Pretreatment_CollapsesWhitespace()
        => Assert.AreEqual("a b", await _service.Pretreatment("a    b"));

    [TestMethod]
    public async Task Pretreatment_AppliesStandardizationRule()
    {
        await Add(SpecialTextType.Standardization, "_", " ");
        Assert.AreEqual("a b", await _service.Pretreatment("a_b"));
    }

    [TestMethod]
    public async Task TryToParseDateTime_StandardFormat_ParsesViaFallback()
    {
        var dt = await _service.TryToParseDateTime("2024-06-15");
        Assert.IsNotNull(dt);
        Assert.AreEqual(new DateTime(2024, 6, 15), dt!.Value.Date);
    }

    [TestMethod]
    public async Task TryToParseDateTime_Garbage_ReturnsNull()
        => Assert.IsNull(await _service.TryToParseDateTime("definitely not a date"));

    [TestMethod]
    public async Task TryToParseDateTime_ConfiguredFormat_Parses()
    {
        await Add(SpecialTextType.DateTime, "yyyy_MM_dd");
        var dt = await _service.TryToParseDateTime("2024_06_15");
        Assert.IsNotNull(dt);
        Assert.AreEqual(new DateTime(2024, 6, 15), dt!.Value.Date);
    }
}
