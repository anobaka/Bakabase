using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.DownloadIntents;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class DownloadIntentCapabilityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SharePoint_public_folders_do_not_make_other_OneDrive_inputs_ready(bool unsupportedFirst)
    {
        DownloadCandidate folder = new() {Id = "folder", Url = "https://tenant.sharepoint.com/:f:/g/personal/folder?e=token"};
        DownloadCandidate consumer = new() {Id = "consumer", Url = "https://onedrive.live.com/?id=personal"};
        var registry = new DownloadMethodRegistry([new PublicProvider()], [], null!, null!);
        var service = new DownloadIntentService(registry, null!, new UserStoragePolicy(false),
            null!, null!, null!, null!, null!, null!);
        var view = await service.PreflightAsync(new()
        {
            Candidates = unsupportedFirst ? [consumer, folder] : [folder, consumer]
        }, Path.GetTempPath(), default);
        Assert.IsTrue(view.Candidates.Single(c => c.Candidate.Id == "folder").CanSelect);
        var unsupported = view.Candidates.Single(c => c.Candidate.Id == "consumer");
        Assert.IsFalse(unsupported.CanSelect);
        Assert.AreEqual("SHAREPOINT_FOLDER_REQUIRED", unsupported.Requirements.Single().Code);
        Assert.IsFalse(unsupported.Methods.Single().Supported);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Google_native_document_support_is_not_cached_as_Drive_file_support(bool documentFirst)
    {
        DownloadCandidate file = new() {Id = "file", Url = "https://drive.google.com/file/d/file-id/view"};
        DownloadCandidate document = new() {Id = "doc", Url = "https://docs.google.com/document/d/document-id/edit"};
        var service = new DownloadIntentService(new([], [], null!, null!), null!, new UserStoragePolicy(false),
            null!, null!, null!, null!, null!, null!);
        var view = await service.PreflightAsync(new()
        {
            Candidates = documentFirst ? [document, file] : [file, document]
        }, Path.GetTempPath(), default);
        Assert.IsTrue(view.Candidates.Single(c => c.Candidate.Id == "file").Methods.Single().Supported);
        var docs = view.Candidates.Single(c => c.Candidate.Id == "doc");
        Assert.IsFalse(docs.Methods.Single().Supported);
        Assert.IsFalse(docs.CanSelect);
        Assert.IsTrue(docs.Requirements.Any(requirement => requirement.Code == "NATIVE_DOCUMENT_EXPORT_UNSUPPORTED"));
    }

    private sealed class PublicProvider : ICloudDownloadProvider
    {
        public string ProviderId => "oneDrive";
        public Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct) =>
            Task.FromResult(new CloudReadiness(true));
        public Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct) =>
            throw new AssertFailedException("Preflight must not download.");
    }
}
