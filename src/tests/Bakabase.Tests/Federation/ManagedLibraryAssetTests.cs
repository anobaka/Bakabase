using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ManagedLibraryAssetTests
{
    [TestMethod]
    public void Attachment_reading_requires_an_actual_attachment_value_not_a_similarly_named_text_property()
    {
        const string path = "/outside/private.png";
        var resource = new Resource
        {
            Properties = new() {{(int) PropertyPool.Custom, new()
            {
                {3, new Resource.Property("Image", PropertyType.SingleLineText,
                    [new(1, path, path, path)])},
                {4, new Resource.Property("Attachments", PropertyType.Attachment,
                    [new(1, new List<string> {"/attachments/saved.png"}, null, null)])}
            }}}
        };
        Assert.IsFalse(ManagedLibraryResourceFiles.IsAttached(resource, path));
        Assert.IsTrue(ManagedLibraryResourceFiles.IsAttached(resource, "/attachments/saved.png"));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsAttached(resource, "/attachments/other.png"));
        Assert.IsFalse(ManagedLibraryResourceFiles.IsAttached(resource, "/attachments/../private.png"));
    }

    [TestMethod]
    public void Upload_payloads_are_bounded_and_torrent_bytes_are_not_treated_as_paths()
    {
        CollectionAssert.AreEqual(new byte[] {1, 2, 3}, ManagedLibraryUploadController.Decode(new("file.torrent", "AQID"), 3));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryUploadController.Decode(new("file", "AQIDBA=="), 3));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryUploadController.Decode(new("file", "invalid!"), 16));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryUploadController.Decode(new("file", ""), 16));
        Assert.ThrowsExactly<FederationAccessException>(() => ManagedLibraryUploadController.Decode(new("", "AQID"), 16));
    }

    [DataTestMethod]
    [DataRow("/management/library/resources/1/attachment", true)]
    [DataRow("/management/library/resources/123/torrent?managedLibraryEpoch=epoch", true)]
    [DataRow("/management/library/resources/0/attachment", false)]
    [DataRow("/management/library/resources/1/attachment/extra", false)]
    [DataRow("/management/library/resources/1/../attachment", false)]
    [DataRow("/file/upload", false)]
    [DataRow("/resource/bulk-delete", false)]
    public void Only_the_named_owner_upload_routes_receive_the_upload_transport_budget(string path, bool allowed) =>
        Assert.AreEqual(allowed, ManagedResourceUploadLimits.IsOwnerUploadPath(path));
}
