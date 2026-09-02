using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Vocabulary-driven text operations: cleaning, removal primitives, trimming and date parsing.
/// </summary>
[TestClass]
public sealed class TextOpsTests
{
    private IServiceProvider _sp = null!;
    private ITextOps _ops = null!;
    private ITextVocabularyService _vocabulary = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _ops = _sp.GetRequiredService<ITextOps>();
        _vocabulary = _sp.GetRequiredService<ITextVocabularyService>();
        await _vocabulary.AddPrefabEntries();
    }

    private async Task AddEntry(WellKnownTextType type, string value1, string? value2 = null)
        => await _vocabulary.AddEntry(await _vocabulary.GetTypeId(type), value1, value2);

    [TestMethod]
    public async Task Clean_CollapsesWhitespace()
        => Assert.AreEqual("a b", await _ops.Clean("a    b"));

    [TestMethod]
    public async Task Clean_AppliesStandardizationMapping()
    {
        await AddEntry(WellKnownTextType.Standardization, "_", " ");
        Assert.AreEqual("a b", await _ops.Clean("a_b"));
    }

    [TestMethod]
    public async Task Clean_RemovesWrappedUselessWords()
    {
        await AddEntry(WellKnownTextType.Useless, "DL版");
        var cleaned = await _ops.Clean("作品名 [DL版]");
        Assert.IsFalse(cleaned.Contains("DL版"), $"expected the wrapped useless word to be gone, got '{cleaned}'");
    }

    [TestMethod]
    public async Task RemoveWrapped_DropsOnlyMatchingSegments()
    {
        var groups = await _vocabulary.AddType("Release groups", TextTypeShape.Values);
        await _vocabulary.AddEntry(groups.Id, "LoliHouse");
        var wrapperTypeId = await _vocabulary.GetTypeId(WellKnownTextType.Wrapper);

        var result = await _ops.RemoveWrapped("[LoliHouse] Title [1080p]", wrapperTypeId, groups.Id,
            TextMatchMode.EqualsAny);

        Assert.AreEqual(" Title [1080p]", result);
    }

    [TestMethod]
    public async Task RemoveWrapped_ContainsMode_MatchesPartialContent()
    {
        var tags = await _vocabulary.AddType("Quality tags", TextTypeShape.Values);
        await _vocabulary.AddEntry(tags.Id, "1080p");
        var wrapperTypeId = await _vocabulary.GetTypeId(WellKnownTextType.Wrapper);

        var result = await _ops.RemoveWrapped("Title [WebRip 1080p HEVC]", wrapperTypeId, tags.Id,
            TextMatchMode.ContainsAny);

        Assert.AreEqual("Title ", result);
    }

    [TestMethod]
    public async Task RemoveWrapped_RejectsNonDelimiterPairTypeAsWrappers()
    {
        var mappings = await _vocabulary.GetTypeId(WellKnownTextType.Standardization);
        var useless = await _vocabulary.GetTypeId(WellKnownTextType.Useless);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _ops.RemoveWrapped("whatever", mappings, useless, TextMatchMode.EqualsAny));
    }

    [TestMethod]
    public async Task RemoveTexts_RemovesEveryOccurrence()
    {
        var noise = await _vocabulary.AddType("Noise", TextTypeShape.Values);
        await _vocabulary.AddEntry(noise.Id, "-repack");

        Assert.AreEqual("Title", await _ops.RemoveTexts("Title-repack", noise.Id, TextMatchMode.ContainsAny));
    }

    [TestMethod]
    public async Task Trim_RemovesEmptyWrappersAndEdgeSeparators()
    {
        var result = await _ops.Trim("Title [] - ", TextTrimOptions.Default);
        Assert.AreEqual("Title", result);
    }

    [TestMethod]
    public async Task TryParseDateTime_StandardFormat_ParsesViaFallback()
    {
        var dt = await _ops.TryParseDateTime("2024-06-15");
        Assert.IsNotNull(dt);
        Assert.AreEqual(new DateTime(2024, 6, 15), dt!.Value.Date);
    }

    [TestMethod]
    public async Task TryParseDateTime_Garbage_ReturnsNull()
        => Assert.IsNull(await _ops.TryParseDateTime("definitely not a date"));

    [TestMethod]
    public async Task TryParseDateTime_ConfiguredFormat_Parses()
    {
        await AddEntry(WellKnownTextType.DateTime, "yyyy_MM_dd");
        var dt = await _ops.TryParseDateTime("2024_06_15");
        Assert.IsNotNull(dt);
        Assert.AreEqual(new DateTime(2024, 6, 15), dt!.Value.Date);
    }

    /// <summary>
    /// StandardValue date conversions reach the configured formats through this binding; losing it
    /// would silently degrade parsing rather than break the build.
    /// </summary>
    [TestMethod]
    public async Task TextOps_IsRegisteredAsTheCustomDateTimeParser()
    {
        await AddEntry(WellKnownTextType.DateTime, "yyyy_MM_dd");
        var parser = _sp.GetRequiredService<ICustomDateTimeParser>();

        var dt = await parser.TryToParseDateTime("2024_06_15");

        Assert.IsNotNull(dt);
        Assert.AreEqual(new DateTime(2024, 6, 15), dt!.Value.Date);
    }
}
