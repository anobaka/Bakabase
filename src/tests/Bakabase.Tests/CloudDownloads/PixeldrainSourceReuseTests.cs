using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PixeldrainSourceReuseTests
{
    private static CloudDownloadRequest PublicRequest() => new("original-operation",
        new CloudSource {Id = "link", ProviderId = "pixeldrain", Url = "https://pixeldrain.com/l/fixture"}, null,
        null, Path.GetTempPath());

    [TestMethod]
    public void Public_share_reuse_uses_the_local_inventory_without_requiring_remote_account_metadata()
    {
        ICloudDownloadSourceReuseProvider provider = new PixeldrainCloudProvider(null!, appService: null!, null!, null!);
        var request = PublicRequest();

        Assert.IsTrue(provider.CanInspectReuseSource(request));
        Assert.IsTrue(provider.CanInspectReuseSource(request with {Source = request.Source with {MethodId = "pixeldrain.galleryDl"}}));
        Assert.IsFalse(provider.CanInspectReuseSource(request with {Source = request.Source with {MethodId = "pixeldrain.rclone"}}));
        Assert.IsFalse(provider.CanInspectReuseSource(request with {AccountId = "some-account"}));
        Assert.IsFalse(provider.CanInspectReuseSource(request with {RemoteEntry = new("pixeldrain", "some-account", "/path", "[]")}));
    }

    [TestMethod]
    public void The_execution_request_uses_independent_private_journals_and_carries_no_write_access_to_the_old_place()
    {
        var root = Path.GetTempPath();
        var request = PublicRequest() with
        {
            ProviderLayoutId = "original-operation:destination:revision",
            OutputDirectory = Path.Combine(root, "new-place"),
        };
        var oldState = WebCloudProvider.StateDirectory(root, "pixeldrain", request.OperationId);
        var newState = WebCloudProvider.StateDirectory(root, "pixeldrain", request.LayoutOperationId);
        var oldOutput = Path.Combine(root, "old-place");
        Assert.AreNotEqual(oldState, newState);
        using var migrated = JsonDocument.Parse(WebCloudProvider.SerializeRequest("pixeldrain", request, newState, null,
            reuseSource: new(oldOutput, oldState, new string('a', 64), true)));
        var body = migrated.RootElement;
        Assert.AreEqual(newState, body.GetProperty("state").GetString());
        Assert.AreEqual(request.OutputDirectory, body.GetProperty("output").GetString());
        var source = body.GetProperty("reuseSource");
        Assert.AreEqual(oldState, source.GetProperty("state").GetString());
        Assert.AreEqual(oldOutput, source.GetProperty("output").GetString());
        Assert.IsTrue(source.GetProperty("disableSourceRead").GetBoolean());
        Assert.IsFalse(body.TryGetProperty("mode", out _));

        using var inspection = JsonDocument.Parse(WebCloudProvider.SerializeRequest("pixeldrain", PublicRequest(), oldState,
            null, mode: "inspectReuseSource"));
        Assert.AreEqual("inspectReuseSource", inspection.RootElement.GetProperty("mode").GetString());
        Assert.IsFalse(inspection.RootElement.TryGetProperty("reuseSource", out _));
    }

    [TestMethod]
    public void Reuse_estimate_distinguishes_partial_network_savings_from_destination_bytes()
    {
        var proof = new string('b', 64);
        var inventory = WebCloudProvider.ReadReuseInventory(JsonSerializer.Serialize(new
        {
            type = "reuseInventory", fileCount = 4, totalBytes = 100L,
            candidateFileCount = 3, candidateBytes = 45L, inventoryFingerprint = proof
        }), 0);
        Assert.AreEqual(proof, inventory.InventoryFingerprint);
        Assert.AreEqual(100L, inventory.Estimate.TotalBytes);
        Assert.AreEqual(55L, inventory.Estimate.RequiredDownloadBytes);

        var denied = Assert.ThrowsException<CloudProviderException>(() => WebCloudProvider.ReadReuseInventory(
            """{"type":"error","message":"DOWNLOAD_DESTINATION_REUSE_SOURCE_CHANGED: secret fixture path"}""", 1));
        Assert.AreEqual("DOWNLOAD_DESTINATION_REUSE_SOURCE_CHANGED", denied.Message);
        var malformed = Assert.ThrowsException<CloudProviderException>(() => WebCloudProvider.ReadReuseInventory(
            """{"type":"reuseInventory","fileCount":1,"totalBytes":2,"candidateFileCount":2,"candidateBytes":3}""", 0));
        Assert.AreEqual("DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE", malformed.Message);
    }

    [TestMethod]
    public void A_missing_destination_budget_differs_from_a_fully_prepared_destination()
    {
        Assert.IsNull(WebCloudProvider.ReadReuseWriteBudget(
            """{"type":"reuseWriteBudget","maximumAdditionalBytes":null}""", 0));
        Assert.AreEqual(0L, WebCloudProvider.ReadReuseWriteBudget(
            """{"type":"reuseWriteBudget","maximumAdditionalBytes":0}""", 0));
        Assert.AreEqual(23L, WebCloudProvider.ReadReuseWriteBudget(
            """{"type":"reuseWriteBudget","maximumAdditionalBytes":23}""", 0));
        var denied = Assert.ThrowsException<CloudProviderException>(() => WebCloudProvider.ReadReuseWriteBudget(
            """{"type":"reuseWriteBudget","maximumAdditionalBytes":-1}""", 0));
        Assert.AreEqual("DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE", denied.Message);
    }
}
