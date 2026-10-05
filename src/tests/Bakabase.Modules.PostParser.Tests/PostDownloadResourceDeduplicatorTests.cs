using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public sealed class PostDownloadResourceDeduplicatorTests
{
    [TestMethod]
    public void MissingLinksRemainIndependentInsteadOfCombiningUnrelatedMetadata()
    {
        var resources = new[] {Link("", "access"), Link(" ", password: "archive"), Link(null!)};
        var result = Deduplicate(resources);
        CollectionAssert.AreEqual(resources, result);
    }

    [TestMethod]
    public void IdenticalLocationsMergeWithoutChangingTheirOrderOrOriginalLinks()
    {
        var first = Link(" HTTPS://Example.COM:443/CaseSensitive?part=1#Section ", "access", "archive");
        var second = Link("magnet:?xt=urn:btih:ABC");
        var result = Deduplicate(first, second,
            Link("https://example.com/CaseSensitive?part=1#Section", "access", "archive"), second);

        Assert.HasCount(2, result);
        Assert.AreEqual(first.Link, result[0].Link);
        Assert.AreEqual(second, result[1]);
    }

    [TestMethod]
    public void ComplementaryCredentialsAndPlanAreCombinedWithoutMutatingTheirInputs()
    {
        var first = Link("https://example.com/file", "access") with {Extraction = new() {Evidence = ["old hint"]}};
        var plan = Plan("archive") with {Evidence = ["old hint", "archive instructions"]};
        var second = Link(first.Link, password: "archive") with {Extraction = plan};
        var result = Deduplicate(first, second).Single();

        Assert.AreEqual("access", result.Code);
        Assert.AreEqual("archive", result.Password);
        Assert.AreEqual("required", result.Extraction!.Requirement);
        Assert.AreEqual(plan.Steps.Single(), result.Extraction.Steps.Single());
        CollectionAssert.AreEqual(new[] {"old hint", "archive instructions"}, result.Extraction.Evidence);
        Assert.IsNull(first.Password);
        Assert.AreEqual("unknown", first.Extraction!.Requirement);
        Assert.IsNull(second.Code);
    }

    [TestMethod]
    [DataRow("code", "other", "password", "password")]
    [DataRow("code", "code", "password", "other")]
    public void ConflictingCredentialsRemainSeparate(string firstCode, string secondCode,
        string firstPassword, string secondPassword)
    {
        Assert.HasCount(2, Deduplicate(Link("https://example.com/file", firstCode, firstPassword),
            Link("https://example.com/file", secondCode, secondPassword)));
    }

    [TestMethod]
    public void ContentGroupConflictsStaySeparateWhileMissingGroupMembershipCanBeCompleted()
    {
        var ungrouped = Link("https://example.test/file", "code");
        var main = ungrouped with {GroupId = "main", Password = "archive"};
        var preview = ungrouped with {GroupId = "preview"};
        var result = Deduplicate(ungrouped, main, preview);

        Assert.HasCount(2, result);
        Assert.AreEqual("main", result[0].GroupId);
        Assert.AreEqual("archive", result[0].Password);
        Assert.AreEqual("preview", result[1].GroupId);
        Assert.IsNull(ungrouped.GroupId);
        Assert.AreEqual("main", Deduplicate(main, ungrouped).Single().GroupId);
    }

    [TestMethod]
    public void ConflictingPipelinesAndRequirementsRemainSeparate()
    {
        var first = Link("https://example.com/file") with {Extraction = Plan("one")};
        Assert.HasCount(2, Deduplicate(first, first with {Extraction = Plan("two")}));
        Assert.HasCount(2, Deduplicate(first, first with {Extraction = new() {Requirement = "notRequired"}}));
        Assert.HasCount(2, Deduplicate(first, first with {Extraction = Plan("one") with
        {
            Steps = [new() {Id = "rename", Op = "renameExtension", Extension = ".7z"},
                new() {Id = "extract", Input = "rename", Op = "extractArchive", Password = "one"}]
        }}));
    }

    [TestMethod]
    public void MatchingPipelinesMergeTheirEvidenceInOrder()
    {
        var first = Link("https://example.com/file") with {Extraction = Plan("archive") with {Evidence = ["one", "two"]}};
        var second = first with {Extraction = Plan("archive") with {Evidence = ["two", "three"]}};
        CollectionAssert.AreEqual(new[] {"one", "two", "three"},
            Deduplicate(first, second).Single().Extraction!.Evidence);
    }

    [TestMethod]
    public void EvidenceBeyondTheValidationLimitIsNotSilentlyDiscarded()
    {
        var first = Link("https://example.com/file") with
        {
            Extraction = Plan("archive") with {Evidence = Enumerable.Range(0, 32).Select(i => i.ToString()).ToList()}
        };
        var next = first with {Extraction = Plan("archive") with {Evidence = ["another"]}};
        Assert.HasCount(2, Deduplicate(first, next));
    }

    [TestMethod]
    [DataRow("https://example.com/Archive", "https://example.com/archive")]
    [DataRow("https://example.com/file?a=1", "https://example.com/file?a=2")]
    [DataRow("https://example.com/file?a=1&b=2", "https://example.com/file?b=2&a=1")]
    [DataRow("https://example.com/file%2fpart", "https://example.com/file/part")]
    [DataRow("https://mega.nz/file/share#first-key", "https://mega.nz/file/share#second-key")]
    [DataRow("https://mega.nz/file/first#key", "https://mega.nz/file/second#key")]
    [DataRow("http://example.com/file", "https://example.com/file")]
    [DataRow("https://example.com/file?pwd=abcd", "https://example.com/file")]
    [DataRow("https://pan.baidu.com/s/share?pwd=abcd&pwd=abcd", "https://pan.baidu.com/s/share")]
    public void MeaningfulUrlDifferencesArePreserved(string first, string next)
    {
        Assert.HasCount(2, Deduplicate(Link(first), Link(next)));
    }

    [TestMethod]
    [DataRow("https://pan.baidu.com/s/share?pwd=abcd", "https://pan.baidu.com/s/share")]
    [DataRow("https://pan.baidu.com/s/share?foo=1&pwd=abcd#fragment", "https://pan.baidu.com/s/share?foo=1#fragment")]
    [DataRow("http://yun.baidu.com:80/s/share?pwd=%61bcd&foo=1", "http://yun.baidu.com/s/share?foo=1")]
    public void BaiduEmbeddedAndSeparateMatchingCodesAreTheSameLocation(string embedded, string separate)
    {
        var first = Deduplicate(Link(embedded), Link(separate, "abcd")).Single();
        Assert.AreEqual(embedded, first.Link);
        Assert.AreEqual("abcd", first.Code);
        var reversed = Deduplicate(Link(separate), Link(embedded)).Single();
        Assert.AreEqual(separate, reversed.Link);
        Assert.AreEqual("abcd", reversed.Code, "Keep the credential when the first URL has no pwd parameter.");
    }

    [TestMethod]
    public void BaiduConflictingEmbeddedOrExplicitCodesRemainSeparate()
    {
        Assert.HasCount(2, Deduplicate(Link("https://pan.baidu.com/s/share?pwd=first"),
            Link("https://pan.baidu.com/s/share?pwd=second")));
        Assert.HasCount(2, Deduplicate(Link("https://pan.baidu.com/s/share?pwd=first", "second"),
            Link("https://pan.baidu.com/s/share", "second")));
    }

    [TestMethod]
    public void MatchingHealthKeepsTheLatestObservationAndConflictingHealthRemainsSeparate()
    {
        var first = Link("https://example.com/file") with
        {
            LinkHealth = new() {Status = "available", Reason = "OK", CheckedAt = DateTimeOffset.UnixEpoch}
        };
        var next = first with {LinkHealth = first.LinkHealth with {CheckedAt = DateTimeOffset.UnixEpoch.AddHours(1)}};
        Assert.AreEqual(next.LinkHealth, Deduplicate(first, next).Single().LinkHealth);
        Assert.HasCount(2, Deduplicate(first, next with {LinkHealth = next.LinkHealth with {Status = "unavailable"}}));
    }

    private static PostDownloadResource Link(string link, string? code = null, string? password = null) =>
        new() {Link = link, Code = code, Password = password};

    private static PostExtractionPlan Plan(string password) => new()
    {
        Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Password = password}]
    };

    private static List<PostDownloadResource> Deduplicate(params PostDownloadResource[] resources) =>
        PostDownloadResourceDeduplicator.Deduplicate(resources);
}
