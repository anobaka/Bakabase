using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for the Regex enhancer — the one resource enhancer that runs purely on
/// the resource's filename with no external data source. Exercises the full
/// CreateEnhancements path: named-group capture, multi-match merging, the
/// no-match / no-expression cases, and the produced enhancement value shape.
/// </summary>
[TestClass]
public sealed class RegexEnhancerTests
{
    private static RegexEnhancer _enhancer = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        _enhancer = new RegexEnhancer(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IFileManager>(),
            sp);
    }

    private static async Task<EnhancementResult> Run(string fileName, List<string>? expressions)
    {
        var resource = new Resource { Path = "/lib/" + fileName };
        var options = new EnhancerFullOptions
        {
            EnhancerId = (int)EnhancerId.Regex,
            Expressions = expressions
        };
        var result = await _enhancer.CreateEnhancements(
            resource, options, new EnhancementLogCollector(), CancellationToken.None);
        Assert.IsNotNull(result);
        return result!;
    }

    private static List<string> GroupValues(EnhancementResult result, string groupName)
        => (List<string>)result.Values!.Single(v => v.DynamicTarget == groupName).Value!;

    private static bool HasNoValues(EnhancementResult result)
        => result.Values == null || result.Values.Count == 0;

    [TestMethod]
    public void Id_IsRegexEnhancer()
        => Assert.AreEqual((int)EnhancerId.Regex, _enhancer.Id);

    [TestMethod]
    public async Task SingleNamedGroup_CapturesValue()
    {
        var result = await Run("[Toei] Dragon Quest", [@"^\[(?<studio>[^\]]+)\]"]);
        CollectionAssert.AreEqual(new[] { "Toei" }, GroupValues(result, "studio"));
    }

    [TestMethod]
    public async Task MultipleNamedGroups_CaptureEachIndependently()
    {
        var result = await Run("[Toei] Dragon Quest", [@"^\[(?<studio>[^\]]+)\]\s+(?<title>.+)$"]);
        Assert.AreEqual(2, result.Values!.Count);
        CollectionAssert.AreEqual(new[] { "Toei" }, GroupValues(result, "studio"));
        CollectionAssert.AreEqual(new[] { "Dragon Quest" }, GroupValues(result, "title"));
    }

    [TestMethod]
    public async Task RepeatedPattern_MergesEveryMatchInOrder()
    {
        var result = await Run("[A][B][C] Movie", [@"\[(?<tag>[^\]]+)\]"]);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, GroupValues(result, "tag"));
    }

    [TestMethod]
    public async Task MultipleExpressions_CaptureFromAllOfThem()
    {
        var result = await Run("[Toei] Show 2023", [@"^\[(?<studio>[^\]]+)\]", @"(?<year>\d{4})"]);
        CollectionAssert.AreEqual(new[] { "Toei" }, GroupValues(result, "studio"));
        CollectionAssert.AreEqual(new[] { "2023" }, GroupValues(result, "year"));
    }

    [TestMethod]
    public async Task Matching_IsCaseInsensitive()
    {
        var result = await Run("[TOEI] Show", [@"\[(?<studio>toei)\]"]);
        CollectionAssert.AreEqual(new[] { "TOEI" }, GroupValues(result, "studio"));
    }

    [TestMethod]
    public async Task NoMatch_ProducesNoValues()
        => Assert.IsTrue(HasNoValues(await Run("plain video.mkv", [@"\[(?<studio>[^\]]+)\]"])));

    [TestMethod]
    public async Task NullExpressions_ProducesNoValues()
        => Assert.IsTrue(HasNoValues(await Run("[Toei] Show", null)));

    [TestMethod]
    public async Task EmptyExpressions_ProducesNoValues()
        => Assert.IsTrue(HasNoValues(await Run("[Toei] Show", [])));

    [TestMethod]
    public async Task EnhancementValue_TargetsCaptureGroupsAsListString()
    {
        var result = await Run("[Toei] Show", [@"\[(?<studio>[^\]]+)\]"]);
        var raw = result.Values!.Single();
        Assert.AreEqual((int)RegexEnhancerTarget.CaptureGroups, raw.Target);
        Assert.AreEqual(StandardValueType.ListString, raw.ValueType);
        Assert.AreEqual("studio", raw.DynamicTarget);
    }
}
