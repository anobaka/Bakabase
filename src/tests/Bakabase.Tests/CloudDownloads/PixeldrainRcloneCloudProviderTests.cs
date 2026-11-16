using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Rclone;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PixeldrainRcloneCloudProviderTests
{
    [DataTestMethod]
    [DataRow("https://pixeldrain.com/d/public-directory", "public-directory", "")]
    [DataRow("https://www.pixeldrain.com/d/public-directory/", "public-directory", "")]
    [DataRow("https://pixeldrain.com/d/public-directory/folder/a.txt", "public-directory", "folder/a.txt")]
    [DataRow("https://pixeldrain.com/d/public-directory/%E6%96%87%E4%BB%B6.txt", "public-directory", "文件.txt")]
    [DataRow("https://pixeldra.in/d/public-directory/folder/a.txt", "public-directory", "folder/a.txt")]
    [DataRow("https://www.pixeldrain.net/d/public-directory/", "public-directory", "")]
    public void PublicFilesystemLocatorPreservesTheExactDirectoryAndRelativePath(string url, string id, string path)
    {
        var locator = PixeldrainRcloneCloudProvider.ParseLocator(url);
        Assert.AreEqual(id, locator.DirectoryId);
        Assert.AreEqual(path, locator.RemotePath);
        Assert.IsTrue(PixeldrainRcloneCloudProvider.CanHandle(url));
    }

    [DataTestMethod]
    [DataRow("https://pixeldrain.com/u/free-file")]
    [DataRow("https://pixeldrain.com/l/free-list")]
    [DataRow("https://pixeldrain.com/d/me")]
    [DataRow("http://pixeldrain.com/d/public-directory")]
    [DataRow("https://other.invalid/d/public-directory")]
    [DataRow("https://pixeldra.in.attacker.test/d/public-directory")]
    [DataRow("https://other.pixeldra.in/d/public-directory")]
    [DataRow("https://pixeldra.in/d/public-directory?api_url=https://other.invalid")]
    [DataRow("https://pixeldrain.net/d/public-directory/folder%2ffile")]
    [DataRow("https://user@pixeldrain.com/d/public-directory")]
    [DataRow("https://pixeldrain.com:8443/d/public-directory")]
    [DataRow("https://pixeldrain.com/d/public-directory?api_url=https://other.invalid")]
    [DataRow("https://pixeldrain.com/d/public-directory#file")]
    [DataRow("https://pixeldrain.com/d/public-directory/../other")]
    [DataRow("https://pixeldrain.com/d/public-directory/%2e%2e/other")]
    [DataRow("https://pixeldrain.com/d/public-directory/folder%2ffile")]
    [DataRow("https://pixeldrain.com/d/public-directory/folder%5cfile")]
    [DataRow("https://pixeldrain.com/d/public-directory/folder\\file")]
    [DataRow("https://pixeldrain.com/d/public-directory/CON.txt")]
    public void FreeSharingOrUnsafeLocatorsNeverBecomeFilesystemDownloads(string url)
    {
        Assert.ThrowsExactly<CloudProviderException>(() => PixeldrainRcloneCloudProvider.ParseLocator(url));
        Assert.IsFalse(PixeldrainRcloneCloudProvider.CanHandle(url));
    }

    [TestMethod]
    public async Task AccountLocatorsAreRejectedBeforeAccessingThePublicEngine()
    {
        var provider = new PixeldrainRcloneCloudProvider(null!, null!, null!);
        var source = new CloudSource { Id = "source", ProviderId = "pixeldrain", MethodId = "pixeldrain.rclone",
            Url = "https://pixeldrain.com/d/public-directory", AccessCode = "code" };
        var request = new CloudDownloadRequest("operation", source, null, null, Path.GetTempPath());
        var account = await Assert.ThrowsExactlyAsync<CloudProviderException>(() =>
            provider.DownloadAsync(request with { AccountId = "account" }, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIXELDRAIN_PUBLIC_DIRECTORY_REQUIRED", account.Message);
    }
}
