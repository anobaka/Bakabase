using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PixeldrainShareAliasTests
{
    [DataTestMethod]
    [DataRow("pixeldra.in", "/u/file-id")]
    [DataRow("www.pixeldra.in", "/l/list-id#item=2")]
    [DataRow("pixeldrain.net", "/u/file-id")]
    [DataRow("www.pixeldrain.net", "/l/list-id#item=2")]
    [DataRow("PIXELDRA.IN", "/d/directory-id/folder/file.txt")]
    public void OfficialAliasesUseTheSameProviderAndArtifactIdentityWithoutChangingTheSavedUrl(string host, string locator)
    {
        var url = "https://" + host + locator;
        var source = DownloadSourceResolver.Normalize(new() {Id = "source", Url = url});
        var canonical = DownloadSourceResolver.Normalize(new() {Id = "source", Url = "https://pixeldrain.com" + locator});

        Assert.AreEqual("pixeldrain", source.ProviderId);
        Assert.AreEqual(canonical.MethodId, source.MethodId);
        Assert.AreEqual(url, source.Url);
        Assert.AreEqual(CloudSourceIdentity.Key(canonical), CloudSourceIdentity.Key(source));
        Assert.AreEqual(AcquisitionDriveKind.Pixeldrain, AcquisitionDriveKinds.Infer(url));
        WebCloudProvider.ValidateShareUrl("pixeldrain", url);
    }

    [DataTestMethod]
    [DataRow("https://pixeldra.in.attacker.test/u/file-id")]
    [DataRow("https://other.pixeldra.in/u/file-id")]
    [DataRow("https://notpixeldra.in/u/file-id")]
    [DataRow("https://pixeldrain.net.attacker.test/u/file-id")]
    public void UnapprovedHostsDoNotBecomeDownloadAdapters(string url)
    {
        Assert.AreEqual("unknown", CloudSourceClassifier.Classify(url));
        Assert.ThrowsExactly<ArgumentException>(() => WebCloudProvider.ValidateShareUrl("pixeldrain", url));
    }

    [TestMethod]
    public void AliasDoesNotChangeTheSelectedAlbumItemOrAcceptAccountPages()
    {
        var alias = DownloadSourceResolver.Normalize(new() {Id = "source", Url = "https://pixeldra.in/l/list-id#item=2"});
        var otherItem = DownloadSourceResolver.Normalize(new() {Id = "source", Url = "https://pixeldrain.com/l/list-id#item=3"});
        Assert.AreNotEqual(CloudSourceIdentity.Key(alias), CloudSourceIdentity.Key(otherItem));
        Assert.AreEqual("unknown", CloudSourceClassifier.Classify("https://pixeldra.in/account"));
    }

    [TestMethod]
    public async Task SavedUnknownAliasesCanBeRecognizedWithoutReparsingOrChangingTheirEvidence()
    {
        var saved = new CloudSource {Id = "saved", ProviderId = "unknown", Kind = "share",
            Url = "https://pixeldra.in/u/file-id", AccessCode = "saved-code", ArchivePassword = "saved-password"};
        var resolved = await new DownloadSourceResolver([]).ResolveAsync(saved, default);
        Assert.AreEqual("pixeldrain", resolved.ProviderId);
        Assert.AreEqual(saved.Url, resolved.Url);
        Assert.AreEqual(saved.AccessCode, resolved.AccessCode);
        Assert.AreEqual(saved.ArchivePassword, resolved.ArchivePassword);
        Assert.AreEqual(saved.ResolutionChain, resolved.ResolutionChain);
    }
}
