using System.Text.Json;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public sealed class PostDownloadMediaTests
{
    [TestMethod]
    public void LegacyScreenshotProjectionPreservesOriginalIndicesAndSavedResults()
    {
        var images = Enumerable.Range(0, 16).Select(i => $"https://images.example.test/{i}.jpg").ToList();
        var content = new PostContent {MainHtml = "<p>Preview screenshots</p>" +
            string.Join("", images.Select(url => $"<img src='{url}'>"))};
        var info = new PostDownloadInfo {Groups = [new() {Id = "preview", Kind = "preview", Evidence = ["Preview screenshots"]}],
            Resources = images.Select(url => new PostDownloadResource {Link = url, GroupId = "preview"}).ToList()};
        info.Resources.Insert(1, new() {Link = "https://example.test/archive.zip"});
        var original = JsonSerializer.Serialize(info);
        var view = PostDownloadMedia.Analyze(content, info);
        Assert.HasCount(16, view.CoverCandidates);
        CollectionAssert.AreEqual(Enumerable.Range(0, 17).Where(i => i != 1).ToArray(), view.ExcludedResourceIndices.ToArray());
        Assert.AreEqual(original, JsonSerializer.Serialize(info), "Compatibility views never rewrite resource identities.");
        var fresh = PostDownloadMedia.SeparateNewResult(content, info);
        Assert.AreEqual("https://example.test/archive.zip", fresh.Resources.Single().Link);
        Assert.HasCount(0, fresh.Groups);
        Assert.HasCount(16, fresh.CoverCandidates);
    }

    [TestMethod]
    public void ImageArchivesExplicitImageDownloadsAndDownloadableTrialsAreNotExcluded()
    {
        const string image = "https://example.test/payload.jpg";
        var content = new PostContent {MainHtml = $"<p>Extract this image archive</p><img src='{image}'><a href='https://example.test/original.png'>Download original</a><img src='https://example.test/original.png'>"};
        var info = new PostDownloadInfo {Groups = [new() {Id = "trial", Kind = "preview"}], Resources =
        [new() {Link = image, GroupId = "trial", Extraction = new() {Requirement = "required", Evidence = ["Extract this image archive"],
            Steps = [new() {Id = "open", Op = "extractArchive"}]}},
         new() {Link = "https://example.test/original.png", GroupId = "trial"},
         new() {Link = "https://example.test/trial.zip", GroupId = "trial"}]};
        var view = PostDownloadMedia.Analyze(content, info);
        Assert.HasCount(0, view.ExcludedResourceIndices);
        Assert.HasCount(0, view.CoverCandidates);
    }

    [TestMethod]
    public void CoverCandidatesRequireCapturedEvidenceAndDoNotCollectAllImages()
    {
        const string url = "https://example.test/cover.jpg?x=a&amp;b";
        var content = new PostContent {MainHtml = "<p>Cover</p><img src='https://example.test/cover.jpg?x=a&amp;amp;b'><img src='https://example.test/avatar.jpg'>"};
        var info = new PostDownloadInfo {CoverCandidates = [new() {Url = url, Kind = "cover", Evidence = ["Cover", "invented"]},
            new() {Url = "https://invented.example.test/cover.jpg"}, new() {Url = "javascript:alert(1)"}, new() {Url = url}]};
        var view = PostDownloadMedia.Analyze(content, info);
        Assert.AreEqual(url, view.CoverCandidates.Single().Url);
        Assert.AreEqual("cover", view.CoverCandidates.Single().Kind);
        CollectionAssert.AreEqual(new[] {"Cover"}, view.CoverCandidates.Single().Evidence);
        Assert.HasCount(0, view.ExcludedResourceIndices);
    }

    [TestMethod]
    public void PlainImageLinksAndHistoricalResultsWithoutSnapshotStayDownloads()
    {
        var info = new PostDownloadInfo {Resources = [new() {Link = "https://example.test/original.jpg", GroupId = "preview"}],
            Groups = [new() {Id = "preview", Kind = "preview"}]};
        Assert.HasCount(0, PostDownloadMedia.Analyze(null, info).ExcludedResourceIndices);
        Assert.HasCount(0, PostDownloadMedia.Analyze(new() {Scope = "pastedText", MainHtml = "https://example.test/original.jpg"}, info).ExcludedResourceIndices);
    }

    [TestMethod]
    public void LazyImagesInCommentsAreSeparatedButNoImageIsFetched()
    {
        const string url = "https://example.test/comment-image";
        var info = new PostDownloadInfo {Resources = [new() {Link = url, GroupId = "preview"}], Groups = [new() {Id = "preview", Kind = "preview"}]};
        var view = PostDownloadMedia.Analyze(new() {Comments = [new() {Html = $"<a href='{url}'><img data-original-src='{url}'></a>"}]}, info);
        CollectionAssert.AreEqual(new[] {0}, view.ExcludedResourceIndices);
        Assert.AreEqual(url, view.CoverCandidates.Single().Url);
    }

    [TestMethod]
    public void ImageOnlySourceIsSeparatedEvenWhenTheModelMislabeledItMain()
    {
        var info = new PostDownloadInfo {Resources = [new() {Link = "https://example.test/screen", GroupId = "main"}],
            Groups = [new() {Id = "main", Kind = "main"}]};
        var content = new PostContent {MainHtml = "<img src='https://example.test/screen'>"};
        var first = PostDownloadMedia.AnalyzeSaved(content, info);
        CollectionAssert.AreEqual(new[] {0}, first.ExcludedResourceIndices);
        first.ExcludedResourceIndices.Clear(); first.CoverCandidates.Clear();
        var cached = PostDownloadMedia.AnalyzeSaved(content, info);
        CollectionAssert.AreEqual(new[] {0}, cached.ExcludedResourceIndices);
        Assert.HasCount(1, cached.CoverCandidates);
        var edited = content with {MainHtml = content.MainHtml + "<a href='https://example.test/screen'>Download original</a>"};
        Assert.HasCount(0, PostDownloadMedia.AnalyzeSaved(edited, info).ExcludedResourceIndices);
    }

    [TestMethod]
    public void NewSavedSchemaUsesSeparatedMetadataWithoutReclassifyingExistingDownloadIdentities()
    {
        var info = new PostDownloadInfo {SchemaVersion = 4, Resources = [new() {Link = "https://example.test/original.jpg"}],
            CoverCandidates = [new() {Url = "https://example.test/cover.jpg", Kind = "cover"}]};
        var view = PostDownloadMedia.AnalyzeSaved(new() {MainHtml = "<img src='https://example.test/original.jpg'>"}, info);
        Assert.HasCount(0, view.ExcludedResourceIndices);
        Assert.AreEqual("https://example.test/cover.jpg", view.CoverCandidates.Single().Url);
    }

    [DataTestMethod]
    [DataRow("main")]
    [DataRow("unknown")]
    public void ExplicitOriginalImageInstructionsWithoutUnpackingStayDownloadable(string groupKind)
    {
        const string instruction = "直接保存下图原文件就是本次资源，无需解压。";
        var info = new PostDownloadInfo {Resources = [new() {Link = "https://example.test/original.png", GroupId = "image",
            Extraction = new() {Requirement = "notRequired", Evidence = [instruction]}}],
            Groups = [new() {Id = "image", Kind = groupKind}]};
        var content = new PostContent {MainHtml = $"<p>{instruction}</p><img src='https://example.test/original.png'>"};
        Assert.HasCount(0, PostDownloadMedia.Analyze(content, info).ExcludedResourceIndices);
        var unsupported = info with {Resources = [info.Resources[0] with
            {Extraction = new() {Requirement = "notRequired", Evidence = ["An invented instruction"]}}]};
        CollectionAssert.AreEqual(new[] {0}, PostDownloadMedia.Analyze(content, unsupported).ExcludedResourceIndices);
    }
}
