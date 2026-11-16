using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class WebCloudDestinationRevisionTests
{
    private string _root = null!;
    private const string Key = "TEST-OPERATION";
    private string PrivateState => Path.Combine(_root, "cloud-downloads", "web", "pixeldrain", Key);
    private CloudReadiness Inspect() => WebCloudProvider.InspectUnstartedOperation(_root, "pixeldrain", Key, default);

    [TestInitialize] public void Setup() => _root = Directory.CreateTempSubdirectory("bb-web-destination-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void Missing_operation_does_not_create_private_directories_or_require_a_dependency()
    {
        Assert.IsTrue(Inspect().Available);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(_root).Any());
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(PrivateState)!, "other"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(PrivateState)!, "other", "completed.json"), "{}");
        Assert.IsTrue(Inspect().Available);
        Assert.IsFalse(Directory.Exists(PrivateState));
    }

    [TestMethod]
    public void Discovery_that_failed_before_writing_its_manifest_can_keep_the_original_download_identity()
    {
        Directory.CreateDirectory(PrivateState);
        var script = Path.Combine(PrivateState, "bridge.py");
        File.WriteAllText(script, "# prior bootstrap");
        Assert.IsTrue(Inspect().Available);
        Assert.AreEqual("# prior bootstrap", File.ReadAllText(script));
        Assert.IsInstanceOfType<ICloudDownloadDestinationRevisionProvider>(new PixeldrainCloudProvider(null!, appService: null!, null!, null!));
    }

    [DataTestMethod]
    [DataRow("manifest.json")]
    [DataRow("completed.json")]
    [DataRow("manifest.new")]
    [DataRow("completed.new")]
    [DataRow("unknown")]
    public void Existing_private_data_is_never_rewritten_or_removed(string name)
    {
        Directory.CreateDirectory(PrivateState);
        var path = Path.Combine(PrivateState, name);
        File.WriteAllText(path, "state");
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_PRESENT", Inspect().Problem);
        Assert.AreEqual("state", File.ReadAllText(path));
    }

    [TestMethod]
    public void Unexpected_directory_and_obstructed_or_unavailable_state_root_are_not_an_empty_operation()
    {
        Directory.CreateDirectory(Path.Combine(PrivateState, "bridge.py"));
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN", Inspect().Problem);
        Directory.Delete(Path.Combine(_root, "cloud-downloads"), true);
        File.WriteAllText(Path.Combine(_root, "cloud-downloads"), "not a directory");
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN", Inspect().Problem);
        Assert.IsFalse(WebCloudProvider.InspectUnstartedOperation(Path.Combine(_root, "missing"), "pixeldrain", Key, default).Available);
    }

    [TestMethod]
    public void Links_in_private_state_or_to_an_absent_bootstrap_script_cannot_prove_absence()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating test symlinks requires a Windows privilege.");
        var outside = Directory.CreateDirectory(Path.Combine(_root, "unrelated")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(_root, "cloud-downloads"), outside);
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN", Inspect().Problem);
        Directory.Delete(Path.Combine(_root, "cloud-downloads"));
        Directory.CreateDirectory(PrivateState);
        File.CreateSymbolicLink(Path.Combine(PrivateState, "bridge.py"), Path.Combine(outside, "missing"));
        Assert.AreEqual("DOWNLOAD_DESTINATION_PROVIDER_STATE_UNKNOWN", Inspect().Problem);
    }
}
