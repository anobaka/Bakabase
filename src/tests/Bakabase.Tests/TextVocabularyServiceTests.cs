using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Vocabulary store: type and entry CRUD, builtin protection, seeding idempotence, and how a
/// type's shape drives set resolution.
/// </summary>
[TestClass]
public sealed class TextVocabularyServiceTests
{
    private IServiceProvider _sp = null!;
    private ITextVocabularyService _vocabulary = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _vocabulary = _sp.GetRequiredService<ITextVocabularyService>();
    }

    [TestMethod]
    public async Task AddType_ThenGetTypes_ReturnsIt()
    {
        await _vocabulary.AddType("Release groups", TextTypeShape.Values, "who released it");

        var types = await _vocabulary.GetTypes();
        var added = types.Single(t => t.Name == "Release groups");
        Assert.IsFalse(added.IsBuiltin);
        Assert.AreEqual(TextTypeShape.Values, added.Shape);
        Assert.AreEqual(0, added.EntryCount);
    }

    [TestMethod]
    public async Task AddType_DuplicateName_Throws()
    {
        await _vocabulary.AddType("Quality tags", TextTypeShape.Values);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _vocabulary.AddType("Quality tags", TextTypeShape.Values));
    }

    [TestMethod]
    public async Task EntryCount_ReflectsAddedEntries()
    {
        var type = await _vocabulary.AddType("Quality tags", TextTypeShape.Values);
        await _vocabulary.AddEntry(type.Id, "1080p");
        await _vocabulary.AddEntries(type.Id, [("WebRip", null), ("HEVC", null)]);

        var reloaded = (await _vocabulary.GetTypes()).Single(t => t.Id == type.Id);
        Assert.AreEqual(3, reloaded.EntryCount);
    }

    [TestMethod]
    public async Task RenameAndDelete_RejectedForBuiltinTypes()
    {
        await _vocabulary.AddPrefabEntries();
        var wrapperId = await _vocabulary.GetTypeId(WellKnownTextType.Wrapper);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _vocabulary.RenameType(wrapperId, "Brackets"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => _vocabulary.DeleteType(wrapperId));
    }

    [TestMethod]
    public async Task DeleteType_RemovesItsEntries()
    {
        var type = await _vocabulary.AddType("Scratch", TextTypeShape.Values);
        await _vocabulary.AddEntry(type.Id, "value");

        await _vocabulary.DeleteType(type.Id);

        Assert.IsFalse((await _vocabulary.GetTypes()).Any(t => t.Id == type.Id));
        Assert.AreEqual(0, (await _vocabulary.GetEntries(type.Id)).Count);
    }

    [TestMethod]
    public async Task AddPrefabEntries_IsIdempotent()
    {
        await _vocabulary.AddPrefabEntries();
        var afterFirst = (await _vocabulary.GetTypes()).Sum(t => t.EntryCount);
        Assert.IsTrue(afterFirst > 0);

        await _vocabulary.AddPrefabEntries();
        var afterSecond = (await _vocabulary.GetTypes()).Sum(t => t.EntryCount);
        Assert.AreEqual(afterFirst, afterSecond);
    }

    [TestMethod]
    public async Task AddPrefabEntries_ToppedUpOneByOne()
    {
        await _vocabulary.AddPrefabEntries();
        var uselessId = await _vocabulary.GetTypeId(WellKnownTextType.Useless);
        var victim = (await _vocabulary.GetEntries(uselessId)).First();

        await _vocabulary.DeleteEntry(victim.Id);
        var afterDelete = (await _vocabulary.GetEntries(uselessId)).Count;

        // A rerun tops the entry back up — the point of this test is that it stays a top-up, i.e.
        // exactly one row returns rather than the whole prefab set being duplicated.
        await _vocabulary.AddPrefabEntries();
        Assert.AreEqual(afterDelete + 1, (await _vocabulary.GetEntries(uselessId)).Count);
    }

    [TestMethod]
    public async Task ResolveSet_PairShape_ExposesPairs()
    {
        await _vocabulary.AddPrefabEntries();

        var set = await _vocabulary.ResolveSet(WellKnownTextType.Wrapper);

        Assert.AreEqual(TextTypeShape.DelimiterPair, set.Shape);
        CollectionAssert.Contains(set.Pairs.Select(p => p.Value1).ToList(), "[");
        Assert.AreEqual("]", set.Pairs.Single(p => p.Value1 == "[").Value2);
    }

    [TestMethod]
    public async Task ResolveSet_ValuesShape_ExposesNoPairsEvenWhenSecondValuesExist()
    {
        await _vocabulary.AddPrefabEntries();

        // Volume prefabs carry an ordinal in the second value that no consumer reads; it must be
        // preserved in storage yet stay out of the resolved pairs.
        var set = await _vocabulary.ResolveSet(WellKnownTextType.Volume);

        Assert.AreEqual(TextTypeShape.Values, set.Shape);
        Assert.AreEqual(0, set.Pairs.Count);
        Assert.IsTrue(set.Values.Count > 0);

        var typeId = await _vocabulary.GetTypeId(WellKnownTextType.Volume);
        Assert.IsTrue((await _vocabulary.GetEntries(typeId)).Any(e => e.Value2 == "1"));
    }

    [TestMethod]
    public async Task PatchEntry_UpdatesOnlyProvidedValues()
    {
        var type = await _vocabulary.AddType("Mappings", TextTypeShape.MappingPair);
        var entry = await _vocabulary.AddEntry(type.Id, "from", "to");

        await _vocabulary.PatchEntry(entry.Id, "changed", null);

        var reloaded = (await _vocabulary.GetEntries(type.Id)).Single();
        Assert.AreEqual("changed", reloaded.Value1);
        Assert.AreEqual("to", reloaded.Value2);
    }
}
