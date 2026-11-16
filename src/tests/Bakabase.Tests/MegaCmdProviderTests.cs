using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Mega;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

namespace Bakabase.Tests;

[TestClass]
public sealed class MegaCmdProviderTests
{
    [TestMethod]
    public void Managed_transfer_can_queue_another_download_but_an_abandoned_server_still_needs_review()
    {
        var marker = Path.GetTempFileName();
        using var gate = new SemaphoreSlim(1, 1);
        try
        {
            gate.Wait();
            Assert.IsFalse(MegaCmdCloudProvider.NeedsServerReview(gate, marker));
            gate.Release();
            Assert.IsTrue(MegaCmdCloudProvider.NeedsServerReview(gate, marker));
            File.Delete(marker);
            Assert.IsFalse(MegaCmdCloudProvider.NeedsServerReview(gate, marker));
            Assert.AreEqual(1, gate.CurrentCount);
        }
        finally { File.Delete(marker); }
    }

    [TestMethod]
    public void Public_share_requires_complete_decryption_key_and_rejects_ambiguous_or_foreign_locators()
    {
        var key = new string('a', 43);
        Assert.IsTrue(MegaPublicLink.IsValid("https://mega.nz/file/12345678#" + key));
        Assert.IsTrue(MegaPublicLink.IsValid("https://mega.nz/#!12345678!" + key));
        Assert.IsTrue(MegaPublicLink.IsValid("https://mega.nz/folder/12345678#" + new string('a', 22) + "/file/abcdefgh"));
        foreach (var url in new[] {"https://mega.nz/file/12345678", "http://mega.nz/file/12345678#" + key,
                     "https://mega.nz.evil.test/file/12345678#" + key, "https://user@mega.nz/file/12345678#" + key,
                     "https://mega.nz/file/12345678?extra=1#" + key, "https://mega.nz/file/12345678#" + key + "\"",
                     "https://mega.nz/file/12345678#" + key + "\n", " https://mega.nz/file/12345678#" + key})
            Assert.IsFalse(MegaPublicLink.IsValid(url), url);
    }

    [TestMethod]
    public void Separately_provided_file_and_folder_keys_are_used_only_for_the_execution_link()
    {
        foreach (var (kind, length) in new[] {("file", 43), ("folder", 22)})
        {
            var url = "https://mega.nz/" + kind + "/12345678";
            var key = new string('a', length - 2) + "_-";
            var source = DownloadSourceResolver.Normalize(new() {Id = "source", Url = url, AccessCode = key});
            var identity = CloudSourceIdentity.Key(source);
            Assert.IsTrue(MegaPublicLink.IsValid(url, key));
            Assert.IsNull(DownloadMethodCatalog.Find("mega", "mega.megacmd")!.ValidateSource!(source));
            Assert.AreEqual(url + "#" + key, MegaPublicLink.Require(source.Url, source.AccessCode));
            Assert.AreEqual(url, source.Url);
            Assert.AreEqual(key, source.AccessCode);
            Assert.AreEqual(identity, CloudSourceIdentity.Key(source), "Execution must not change the retained operation identity.");
        }
    }

    [TestMethod]
    public void An_existing_fragment_is_never_replaced_by_a_separate_key()
    {
        var key = new string('a', 43); var other = new string('b', 43);
        var full = "https://mega.nz/file/12345678#" + key;
        Assert.AreEqual(full, MegaPublicLink.Require(full, other));
        var legacy = "https://mega.nz/#!12345678!" + key;
        Assert.AreEqual(legacy, MegaPublicLink.Require(legacy, other));
        foreach (var url in new[] {"https://mega.nz/file/12345678#", "https://mega.nz/file/12345678#short",
                     "https://mega.nz/#!12345678", "https://mega.nz/folder/12345678#short/file/abcdefgh"})
        {
            Assert.IsFalse(MegaPublicLink.IsValid(url, other));
            Assert.ThrowsException<CloudProviderException>(() => MegaPublicLink.Require(url, other));
        }
    }

    [TestMethod]
    public void Separate_keys_cannot_repair_wrong_hosts_paths_or_inject_command_protocol_characters()
    {
        var url = "https://mega.nz/file/12345678"; var key = new string('a', 43);
        foreach (var badKey in new[] {"", new string('a', 22), key + "=", key + " ", key + "\n",
                     key[..42] + "'", key[..42] + "\"", key[..42] + "\\", key[..42] + "/", key[..42] + "#"})
            Assert.IsFalse(MegaPublicLink.IsValid(url, badKey));
        foreach (var badUrl in new[] {"https://mega.nz.evil.test/file/12345678", "http://mega.nz/file/12345678",
                     "https://user@mega.nz/file/12345678", "https://mega.nz/file/12345678?extra=1",
                     "https://mega.nz/file/1234567", "https://mega.nz/file/12345678/extra",
                     "https://mega.nz/folder/12345678", "https://mega.nz:444/file/12345678"})
            Assert.IsFalse(MegaPublicLink.IsValid(badUrl, key));
        Assert.IsNotNull(DownloadMethodCatalog.Find("mega", "mega.megacmd")!.ValidateSource!(
            new() {Id = "source", ProviderId = "mega", Url = url, AccessCode = "short"}));
    }

    [TestMethod]
    public void MEGA_quota_and_access_are_not_classified_as_deleted_shares()
    {
        Assert.AreEqual("MEGA_TRANSFER_QUOTA", MegaCmdCloudProvider.Classify("Overquota: bandwidth limit"));
        Assert.AreEqual("MEGA_ACCESS_REQUIRED", MegaCmdCloudProvider.Classify("Access denied"));
        Assert.AreEqual("MEGA_NETWORK_ERROR", MegaCmdCloudProvider.Classify("Connection timeout"));
        Assert.AreEqual("MEGA_DECRYPTION_FAILED", MegaCmdCloudProvider.Classify("Decryption failed"));
        Assert.AreEqual("MEGA_DOWNLOAD_FAILED", MegaCmdCloudProvider.Classify("Shared library not found", 127));
        Assert.AreEqual("MEGA_DOWNLOAD_FAILED", MegaCmdCloudProvider.Classify("Request incomplete"));
        Assert.AreEqual("MEGA_SHARE_UNAVAILABLE", MegaCmdCloudProvider.Classify("Resource does not exist", 9));
        Assert.AreEqual("MEGA_DECRYPTION_FAILED", MegaCmdCloudProvider.Classify("Cryptographic error", 14));
    }

    [TestMethod]
    public void Existing_user_profile_and_partial_download_are_never_adopted_or_overwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "mega-ownership-" + Guid.NewGuid().ToString("N"));
        try
        {
            var native = Path.Combine(root, "native"); var managed = Path.Combine(root, "managed");
            Directory.CreateDirectory(native); File.WriteAllText(Path.Combine(native, "session"), "keep");
            Assert.ThrowsException<InvalidOperationException>(() => MegaCmdProfile.Ensure(native, managed, true));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(native, "session")));
            Assert.ThrowsException<CloudProviderException>(() => MegaCmdCloudProvider.RequireEmptyOutput(native));
            Assert.IsFalse(Directory.Exists(managed));
        }
        finally {if (Directory.Exists(root)) Directory.Delete(root, true);}
    }

    [TestMethod]
    public void Managed_profile_accepts_only_exact_owned_link_without_changing_home()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "mega-profile-" + Guid.NewGuid().ToString("N"));
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        Directory.CreateDirectory(root);
        try
        {
            var native = Path.Combine(root, "native"); var managed = Path.Combine(root, "managed");
            MegaCmdProfile.Ensure(native, managed, false); Assert.IsFalse(Directory.Exists(native));
            MegaCmdProfile.Ensure(native, managed, true); MegaCmdProfile.Ensure(native, managed, false);
            Assert.AreEqual(Path.GetFullPath(managed), new DirectoryInfo(native).ResolveLinkTarget(true)!.FullName);
            Assert.ThrowsException<InvalidOperationException>(() => MegaCmdProfile.Ensure(native, Path.Combine(root, "other"), true));
            Assert.AreEqual(originalHome, Environment.GetEnvironmentVariable("HOME"));
            Directory.Delete(native);
        }
        finally {if (Directory.Exists(root)) Directory.Delete(root, true);}
    }

    [TestMethod]
    public void Public_MEGA_and_PikPak_share_capabilities_do_not_replace_account_remote_routes()
    {
        Assert.IsNull(DownloadMethodCatalog.Find("mega", "mega.megacmd")!.AccountProvider);
        Assert.AreEqual("pikpakCli", DownloadMethodCatalog.Find("pikpak", "pikpak.share")!.AccountProvider);
        Assert.IsTrue(DownloadMethodCatalog.Find("pikpak", "pikpak.rclone")!.RemoteOnly);
        Assert.AreEqual("megacmd", DownloadMethodCatalog.Find("mega", "mega.megacmd")!.EngineId);
    }

    [TestMethod]
    public void Unmarked_managed_session_and_forged_marker_are_not_adopted()
    {
        var root = Path.Combine(Path.GetTempPath(), "mega-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var native = Path.Combine(root, "native"); var managed = Path.Combine(root, "managed");
            Directory.CreateDirectory(managed); File.WriteAllText(Path.Combine(managed, "session"), "keep");
            Assert.ThrowsException<InvalidOperationException>(() => MegaCmdProfile.Ensure(native, managed, false));
            Assert.ThrowsException<InvalidOperationException>(() => MegaCmdProfile.Ensure(native, managed, true));
            File.WriteAllText(Path.Combine(managed, ".bakabase-megacmd-profile"), "1");
            Assert.ThrowsException<InvalidOperationException>(() => MegaCmdProfile.Ensure(native, managed, true));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(managed, "session")));
            Assert.IsFalse(Directory.Exists(native));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Upstream_text_protocol_cannot_accept_quotes_controls_or_unsupported_proxy_schemes()
    {
        MegaCmdCloudProvider.ValidateProxy(null);
        MegaCmdCloudProvider.ValidateProxy(new("https://localhost:8080", "user", "ordinary-password"));
        foreach (var proxy in new[] { new CloudToolProxy.Settings("socks5://localhost:1080", null, null),
                     new("http://localhost:8080", "user", "quoted\"password"), new("http://localhost:8080", "domain\\user", "password"),
                     new("http://localhost:8080", "user", "line\nbreak") })
            Assert.ThrowsException<CloudProviderException>(() => MegaCmdCloudProvider.ValidateProxy(proxy));
        Assert.ThrowsException<CloudProviderException>(() => MegaCmdCloudProvider.RequireEmptyOutput("/a/quoted\"directory"));
    }

    [TestMethod]
    public void Transfer_progress_filters_other_operations_and_keeps_rounded_sizes_out_of_byte_metadata()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mega-progress"));
        var sample = "TAG|DESTINYPATH|PROGRESS|STATE\n" +
                     $"1|{root}/a.bin|50.00% of 2.00 GB|ACTIVE\n" +
                     $"2|{root}/b.bin|100.00% of 2.00 GB|COMPLETED\n" +
                     $"3|{root}-other/c.bin|0.00% of 2.00 GB|ACTIVE\n";
        Assert.AreEqual(75d, MegaCmdCloudProvider.ReadProgress(sample, root));
        Assert.IsNull(MegaCmdCloudProvider.ReadProgress(sample + "Showing first 1000 transfers", root));
        Assert.IsNull(MegaCmdCloudProvider.ReadProgress(sample.Replace("50.00%", "----%"), root));
        Assert.IsNull(MegaCmdCloudProvider.ReadProgress(sample.Replace("a.bin", "ambiguous|name"), root));
        Assert.IsNull(MegaCmdCloudProvider.ReadProgress("No transfers", root));
    }
}
