using System.Diagnostics;
using System.Text.Json;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.TestKit.Implementations;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed partial class RcloneRemoteEngineTests
{
    private string _root = null!;
    private string _account = null!;
    private string _profile = null!;
    private RcloneRemoteEngine _engine = null!;
    private NetworkOptions _network = null!;
    private const string Manifest = """[{"Path":"a.txt","Size":3,"ID":"one","ModTime":"2026-10-20T00:00:00Z","Hashes":{"MD5":"900150983cd24fb0d6963f7d28e17f72"}}]""";

    [TestInitialize]
    public async Task Setup()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix process fixture; matrix/path tests are portable.");
        _root = Path.Combine(Path.GetTempPath(), "rclone-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root); _account = Guid.NewGuid().ToString("N");
        _profile = Path.Combine(_root, "private", "pikpak", _account);
        var executable = Path.Combine(_root, "fake-rclone");
        await File.WriteAllTextAsync(executable, """
            #!/bin/sh
            case "$1" in
              obscure) IFS= read -r value; printf 'opaque-password\n' ;;
              lsjson) printf '%s\n' "$@" > listing-arguments.log; cat manifest.json ;;
              copy)
                printf 'copy\n' >> copies.log
                printf '%s\n' "$@" > arguments.log
                printf '%s\n' "$HTTP_PROXY" > proxy.log
                case "$(cat mode 2>/dev/null)" in
                  cancel) printf '%s' "$$" > pid; while :; do sleep 1; done ;;
                  failure) printf 'https://secret.invalid/?access_token=must-not-leak' >&2; exit 9 ;;
                  bad) printf def > "$3/a.txt" ;;
                  *) printf abc > "$3/a.txt" ;;
                esac
                if [ "$(cat mode 2>/dev/null)" = changed ]; then printf '[]' > manifest.json; fi
                printf '{"stats":{"bytes":3,"totalBytes":3,"transfers":1,"totalTransfers":1},"msg":"secret"}\n' >&2
                ;;
              *) exit 21 ;;
            esac
            """);
        File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
        _network = new();
        var proxy = new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(_network));
        _engine = new(Path.Combine(_root, "private"), _ => Task.FromResult(executable), proxy);
        await _engine.ConfigurePikPakAsync(_account, "fixture@example.invalid", "only-on-stdin", default);
        await File.WriteAllTextAsync(Path.Combine(_profile, "manifest.json"), Manifest);
    }

    [TestCleanup]
    public void Cleanup() { if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task CredentialsStayPrivateAndDoNotEnterProcessArguments()
    {
        var config = Path.Combine(_profile, "rclone.conf");
        var text = await File.ReadAllTextAsync(config);
        Assert.IsTrue(text.Contains("pass = opaque-password")); Assert.IsFalse(text.Contains("only-on-stdin"));
        Assert.IsTrue(text.Contains("no_media_link = true"), "Account downloads must request original file links.");
        Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(config));
        var info = _engine.Start("rclone", _profile, ["lsjson", "managed:"], null);
        Assert.IsFalse(string.Join(' ', info.ArgumentList).Contains("only-on-stdin"));
        Assert.IsFalse(info.Environment.Keys.Any(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task CallerWorkspaceIsExplicitAndVerifiedDeliverySurvivesAnotherCallersPreference()
    {
        var workspace = Path.Combine(_root, "caller-workspace"); Directory.CreateDirectory(workspace);
        var profile = new RcloneProfileRef("pikpak", _account);
        var files = await _engine.ListAsync(profile, "folder", default);
        var target = Path.Combine(_root, "destination");
        await _engine.CopyAsync(profile, "folder", target, files, null, default, workspaceDirectory: workspace);
        var stage = File.ReadAllLines(Path.Combine(_profile, "arguments.log"))[2];
        StringAssert.StartsWith(stage, Path.Combine(workspace, ".bakabase-work"));
        var reused = await _engine.CopyAsync(profile, "folder", target, files, null, default,
            workspaceDirectory: Path.Combine(_root, "disconnected-caller-root"));
        Assert.IsTrue(reused.Reused);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(reused.Files.Single().Path));
    }

    [TestMethod]
    public async Task CopyVerifiesExactHashesPublishesAndReusesWithoutRecopy()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination"); RcloneCopyProgress? progress = null;
        var first = await _engine.CopyAsync(_account, "folder", target, files, p => progress = p, null, default);
        Assert.IsFalse(first.Reused); Assert.AreEqual("abc", await File.ReadAllTextAsync(first.Files.Single().Path));
        Assert.AreEqual(64, first.Files.Single().Sha256.Length); Assert.AreEqual(3L, progress?.Bytes);
        var second = await _engine.CopyAsync(_account, "folder", target, files, null, null, default);
        Assert.IsTrue(second.Reused); Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
        Assert.IsFalse((await File.ReadAllTextAsync(Path.Combine(_profile, "arguments.log"))).Contains("only-on-stdin"));
    }

    [TestMethod]
    public async Task BulkStagingAndRcloneTemporaryFilesUseDestinationStorageAndAreReclaimedAfterDelivery()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination");
        await _engine.CopyAsync(_account, "folder", target, files, null, null, default);
        var arguments = File.ReadAllLines(Path.Combine(_profile, "arguments.log"));
        var stage = arguments[2];
        StringAssert.StartsWith(stage, Path.Combine(_root, ".bakabase-work"));
        Assert.IsFalse(stage.StartsWith(Path.Combine(_root, "private") + Path.DirectorySeparatorChar));
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(stage)!, "temporary"), arguments[Array.IndexOf(arguments, "--temp-dir") + 1]);
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(stage)!, "cache"), arguments[Array.IndexOf(arguments, "--cache-dir") + 1]);
        Assert.IsTrue(Directory.Exists(arguments[Array.IndexOf(arguments, "--temp-dir") + 1]));
        Assert.IsFalse(File.Exists(Path.Combine(stage, "a.txt")));
        Assert.AreEqual("abc", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        Assert.IsTrue(File.Exists(Directory.GetFiles(_profile, "completed.json", SearchOption.AllDirectories).Single()));
        Assert.IsTrue(File.Exists(Path.Combine(_profile, "rclone.conf")));
    }

    [TestMethod]
    public async Task CompletedDeliveryReusesItsReceiptWithoutRemoteAccessOrAutomaticRedownloadAfterRemoval()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination");
        await _engine.CopyAsync(_account, "folder", target, files, null, null, default);
        await File.WriteAllTextAsync(Path.Combine(_profile, "manifest.json"), "invalid remote response");
        Assert.IsTrue((await _engine.CopyAsync(_account, "folder", target, files, null, null, default)).Reused);
        File.Delete(Path.Combine(target, "a.txt"));
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.CopyAsync(_account, "folder", target, files, null, null, default));
        Assert.AreEqual("RCLONE_LOCAL_CHANGED", error.Code);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
    }

    [TestMethod]
    public async Task CustomWorkspaceStaysPinnedAcrossDisconnectAndConfigurationChanges()
    {
        var firstRoot = Path.Combine(_root, "workspace-a"); Directory.CreateDirectory(firstRoot);
        var secondRoot = Path.Combine(_root, "workspace-b"); Directory.CreateDirectory(secondRoot);
        string? selected = firstRoot;
        var engine = new RcloneRemoteEngine(Path.Combine(_root, "private"), _ => Task.FromResult(Path.Combine(_root, "fake-rclone")),
            new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(_network)), workspaceRoot: () => selected);
        var files = await engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination");
        await File.WriteAllTextAsync(Path.Combine(_profile, "mode"), "failure");
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => engine.CopyAsync(_account, "folder", target, files, null, null, default));
        var stage = File.ReadAllLines(Path.Combine(_profile, "arguments.log"))[2];
        StringAssert.StartsWith(stage, Path.Combine(firstRoot, ".bakabase-work"));
        Directory.Move(firstRoot, firstRoot + ".detached");
        selected = secondRoot;
        File.Delete(Path.Combine(_profile, "mode"));
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => engine.CopyAsync(_account, "folder", target, files, null, null, default));
        Assert.IsFalse(Directory.Exists(firstRoot));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(secondRoot).Length);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
        Directory.Move(firstRoot + ".detached", firstRoot);
        await engine.CopyAsync(_account, "folder", target, files, null, null, default);
        Assert.AreEqual(stage, File.ReadAllLines(Path.Combine(_profile, "arguments.log"))[2]);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
    }

    [TestMethod]
    public async Task ExistingLegacyVerifiedStageRemainsReusableAndOnlyItsReceiptFilesAreReclaimed()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination"); Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "conflict");
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => _engine.CopyAsync(_account, "folder", target, files, null, null, default));
        var operation = Path.GetDirectoryName(Directory.GetFiles(_profile, "completed.json", SearchOption.AllDirectories).Single())!;
        var stage = File.ReadAllLines(Path.Combine(_profile, "arguments.log"))[2];
        var legacy = Path.Combine(operation, "files");
        Directory.Move(stage, legacy); File.Delete(Path.Combine(operation, "workspace.json"));
        var incomplete = Path.Combine(operation, "incomplete-old"); Directory.CreateDirectory(incomplete);
        await File.WriteAllTextAsync(Path.Combine(incomplete, "evidence"), "retain");
        File.Delete(Path.Combine(target, "a.txt"));
        Assert.IsTrue((await _engine.CopyAsync(_account, "folder", target, files, null, null, default)).Reused);
        Assert.IsFalse(Directory.Exists(legacy));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(Path.Combine(incomplete, "evidence")));
        Assert.IsTrue(File.Exists(Path.Combine(operation, "completed.json")));
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
    }

    [TestMethod]
    public async Task ConflictDoesNotOverwriteAndCanRetryImmediatelyUsingVerifiedStage()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination"); Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "a.txt"), "keep");
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.CopyAsync(_account, "folder", target, files, null, null, default));
        Assert.AreEqual("RCLONE_DESTINATION_CONFLICT", error.Message);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(target, "a.txt")));
        File.Delete(Path.Combine(target, "a.txt"));
        Assert.IsTrue((await _engine.CopyAsync(_account, "folder", target, files, null, null, default)).Reused);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
    }

    [TestMethod]
    public async Task ReconfiguringAnAccountInvalidatesThePreviousCredentialsReceipt()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        var target = Path.Combine(_root, "destination");
        await _engine.CopyAsync(_account, "folder", target, files, null, null, default);
        await _engine.ConfigurePikPakAsync(_account, "another@example.invalid", "new-private-secret", default);
        var result = await _engine.CopyAsync(_account, "folder", target, files, null, null, default);
        Assert.IsFalse(result.Reused);
        Assert.AreEqual(2, File.ReadAllLines(Path.Combine(_profile, "copies.log")).Length);
    }

    [TestMethod]
    public async Task SnapshotVersionRejectsAnAccountChangeBeforeListOrCopy()
    {
        var profile = new RcloneProfileRef("pikpak", _account);
        var version = await _engine.GetProfileVersionAsync(profile, default);
        var files = await _engine.ListAsync(profile, "folder", default, version);
        await _engine.ConfigurePikPakAsync(_account, "replacement@example.invalid", "replacement-secret", default);
        var listError = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.ListAsync(profile, "folder", default, version));
        var target = Path.Combine(_root, "destination");
        var copyError = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.CopyAsync(profile, "folder", target, files, null, default, version));
        Assert.AreEqual("RCLONE_ACCOUNT_CHANGED", listError.Code);
        Assert.AreEqual("RCLONE_ACCOUNT_CHANGED", copyError.Code);
        Assert.IsFalse(File.Exists(Path.Combine(_profile, "copies.log")));
        Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task ProfilesAreScopedByProviderAndLogoutPreservesVerifiedCopies()
    {
        var pikpak = new RcloneProfileRef("pikpak", _account);
        var oneDrive = new RcloneProfileRef("oneDrive", _account);
        await _engine.ConfigureOneDriveAsync(_account, new()
        {
            TokenJson = """{"access_token":"private-access","refresh_token":"private-refresh","unexpected":"ignored"}""",
            DriveId = "drive-id", DriveType = "business", ClientSecret = "private-client-secret"
        }, default);
        Assert.IsTrue(await _engine.HasProfileAsync(pikpak, default));
        Assert.IsTrue(await _engine.HasProfileAsync(oneDrive, default));
        Assert.AreNotEqual(await _engine.GetProfileVersionAsync(pikpak, default),
            await _engine.GetProfileVersionAsync(oneDrive, default));
        var directory = Path.Combine(_root, "private", "oneDrive", _account);
        var config = await File.ReadAllTextAsync(Path.Combine(directory, "rclone.conf"));
        Assert.IsTrue(config.Contains("type = onedrive"));
        Assert.IsTrue(config.Contains("Files.Read Files.Read.All Sites.Read.All offline_access"));
        Assert.IsTrue(config.Contains("client_secret = private-client-secret"), "OneDrive's native OAuth option consumes the literal secret, not an obscured password.");
        Assert.IsFalse(config.Contains("client_secret = opaque-password"));
        Assert.IsFalse(config.Contains("unexpected"));
        Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(directory, "rclone.conf")));
        var files = await _engine.ListAsync(pikpak, "folder", default);
        var result = await _engine.CopyAsync(pikpak, "folder", Path.Combine(_root, "destination"), files, null, default);
        await _engine.RemoveProfileAsync(pikpak, default);
        Assert.IsFalse(await _engine.HasProfileAsync(pikpak, default));
        Assert.IsNull(await _engine.GetProfileVersionAsync(pikpak, default));
        Assert.IsTrue(await _engine.HasProfileAsync(oneDrive, default));
        Assert.IsTrue(Directory.GetFiles(Path.Combine(_profile, "copies"), "completed.json", SearchOption.AllDirectories).Any());
        Assert.AreEqual("abc", await File.ReadAllTextAsync(result.Files.Single().Path));
    }

    [TestMethod]
    public async Task MissingProfileQueriesDoNotCreateAProfileDirectory()
    {
        var profile = new RcloneProfileRef("oneDrive", Guid.NewGuid().ToString("N"));
        Assert.IsFalse(await _engine.HasProfileAsync(profile, default));
        Assert.IsNull(await _engine.GetProfileVersionAsync(profile, default));
        await _engine.RemoveProfileAsync(profile, default);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "private", "oneDrive")));
    }

    [TestMethod]
    public async Task OneDriveConfigurationCannotInjectAnArbitraryBackendOrEndpoint()
    {
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => _engine.ConfigureOneDriveAsync(_account,
            new() { TokenJson = """{"access_token":"private-access"}""", DriveId = "drive\napi_url = https://invalid.invalid" }, default));
        Assert.AreEqual("RCLONE_ONEDRIVE_CONFIGURATION_INVALID", error.Code);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "private", "oneDrive")));
        Assert.IsFalse(new RcloneOneDriveConfiguration { TokenJson = "private-access" }.ToString().Contains("private-access"));
    }

    [TestMethod]
    public async Task PublicPixeldrainProfileIsAnonymousIdempotentAndUsesThePinnedBackendConfigKey()
    {
        var first = await _engine.EnsurePixeldrainPublicProfileAsync("public-directory", default);
        var version = await _engine.GetProfileVersionAsync(first, default);
        var second = await _engine.EnsurePixeldrainPublicProfileAsync("public-directory", default);
        Assert.AreEqual(first, second);
        Assert.AreEqual(version, await _engine.GetProfileVersionAsync(second, default));
        var config = await File.ReadAllTextAsync(Path.Combine(_root, "private", "pixeldrain", first.Id, "rclone.conf"));
        Assert.IsTrue(config.Contains("root_folder_id = public-directory"));
        Assert.IsFalse(config.Contains("directory_id"));
        Assert.IsFalse(config.Contains("api_key"));
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => _engine.EnsurePixeldrainPublicProfileAsync("me", default));
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() => _engine.ConfigurePixeldrainAsync(_account, "me", null, default));
    }

    [TestMethod]
    public async Task PublicDirectoryProviderUsesTheRealEngineAndReturnsOnlyVerifiedFiles()
    {
        var profile = await _engine.EnsurePixeldrainPublicProfileAsync("public-directory", default);
        var directory = Path.Combine(_root, "private", "pixeldrain", profile.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), Manifest);
        var provider = new PixeldrainRcloneCloudProvider(_engine, null!, new(Path.Combine(_root, "snapshots")));
        var request = new CloudDownloadRequest("operation", new() { Id = "source", ProviderId = "pixeldrain",
            Url = "https://pixeldrain.com/d/public-directory", MethodId = "pixeldrain.rclone" }, null, null,
            Path.Combine(_root, "destination"));
        var first = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        var second = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(first.Files.Single()));
        CollectionAssert.AreEqual(first.Files.ToArray(), second.Files.ToArray());
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(directory, "copies.log")).Length);
        Assert.AreEqual("pixeldrain.rclone", provider.MethodId);
        Assert.IsFalse(((ICloudDownloadProvider)provider).SupportsTransfer);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MigratedDestinationKeepsEngineAndProviderSnapshotIdentity(bool publicDirectory)
    {
        ICloudDownloadProvider provider;
        CloudSource source;
        string profileDirectory;
        if (publicDirectory)
        {
            var profile = await _engine.EnsurePixeldrainPublicProfileAsync("public-directory", default);
            profileDirectory = Path.Combine(_root, "private", "pixeldrain", profile.Id);
            await File.WriteAllTextAsync(Path.Combine(profileDirectory, "manifest.json"), Manifest);
            provider = new PixeldrainRcloneCloudProvider(_engine, null!, new(Path.Combine(_root, "snapshots")));
            source = new() {Id = "source", ProviderId = "pixeldrain", Url = "https://pixeldrain.com/d/public-directory", MethodId = "pixeldrain.rclone"};
        }
        else
        {
            var (accounts, account, profile) = await AccountFixture();
            profileDirectory = profile;
            provider = new RclonePikPakCloudProvider(_engine, accounts, new(Path.Combine(_root, "snapshots")));
            var registry = new DownloadMethodRegistry([provider], [], null!, null!, rcloneAccounts: accounts);
            source = await registry.BindAccountAsync(DownloadSourceResolver.Normalize(new()
                {Id = "owned", Kind = "remote", ProviderId = "pikpak", Url = "/folder", MethodId = "pikpak.rclone", AccountId = account.Id}), null, default);
        }
        var old = Path.Combine(_root, "old-output");
        var destination = Path.Combine(_root, "new-output");
        var request = new CloudDownloadRequest("migration-operation", source, source.AccountId, null, old);
        await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Directory.Move(old, destination);
        var metadata = Path.Combine(_root, "private", "artifact-workspaces"); Directory.CreateDirectory(metadata);
        await File.WriteAllTextAsync(Path.Combine(metadata, "relocations.json"), JsonSerializer.Serialize(
            new FileProcessingWorkspaceResolver.Relocations(1, [new(old, destination)]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await File.WriteAllTextAsync(Path.Combine(profileDirectory, "manifest.json"), "remote unavailable");
        var result = await provider.DownloadAsync(request with {OutputDirectory = destination}, _ => Task.CompletedTask, default);
        Assert.AreEqual(Path.Combine(destination, "a.txt"), result.Files.Single());
        Assert.AreEqual("abc", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(profileDirectory, "copies.log")).Length);
    }

    [TestMethod]
    public async Task PublicDirectoryRetryCannotAdoptChangedRemoteFilesAfterAnInterruptedAttempt()
    {
        var profile = await _engine.EnsurePixeldrainPublicProfileAsync("public-directory", default);
        var directory = Path.Combine(_root, "private", "pixeldrain", profile.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), Manifest);
        await File.WriteAllTextAsync(Path.Combine(directory, "mode"), "failure");
        var storeRoot = Path.Combine(_root, "snapshots");
        var provider = new PixeldrainRcloneCloudProvider(_engine, null!, new(storeRoot));
        var request = new CloudDownloadRequest("operation", new() { Id = "source", ProviderId = "pixeldrain",
            Url = "https://pixeldrain.com/d/public-directory", MethodId = "pixeldrain.rclone" }, null, null,
            Path.Combine(_root, "destination"));
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), Manifest.Replace("\"Size\":3", "\"Size\":4"));
        File.Delete(Path.Combine(directory, "mode"));
        // New store/provider instances model restart; the original frozen listing survives.
        provider = new(_engine, null!, new(storeRoot));
        var error = await Assert.ThrowsExactlyAsync<CloudProviderException>(() =>
            provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("RCLONE_REMOTE_CHANGED", error.Message);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(directory, "copies.log")).Length);
        Assert.IsFalse(Directory.Exists(request.OutputDirectory));
    }

    [TestMethod]
    public async Task FileRemoteAndRootDirectoryUseNativeRcloneLocatorSemantics()
    {
        var profile = new RcloneProfileRef("pikpak", _account);
        var files = await _engine.ListAsync(profile, "a.txt", default);
        await _engine.CopyAsync(profile, "a.txt", Path.Combine(_root, "file-out"), files, null, default);
        Assert.AreEqual("managed:a.txt", File.ReadAllLines(Path.Combine(_profile, "arguments.log"))[1]);
        await _engine.ListAsync(profile, "/", default);
        Assert.AreEqual("managed:", File.ReadAllLines(Path.Combine(_profile, "listing-arguments.log"))[1]);
        Assert.AreEqual("managed:", RcloneRemoteEngine.Remote("/"));
        Assert.AreEqual("managed:folder/a.txt", RcloneRemoteEngine.Remote("/folder/a.txt"));
    }

    [TestMethod]
    public void ToolProxyInheritsTheActualProviderAndExplicitDirectWins()
    {
        _network.CustomProxies = [new() { Id = "pikpak", Address = "http://127.0.0.1:8101" },
            new() { Id = "onedrive", Address = "http://127.0.0.1:8102" },
            new() { Id = "pixeldrain", Address = "http://127.0.0.1:8103" }];
        _network.ThirdPartyProxies = new()
        {
            [(int)ThirdPartyId.PikPak] = new() { Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "pikpak" },
            [(int)ThirdPartyId.OneDrive] = new() { Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "onedrive" },
            [(int)ThirdPartyId.Pixeldrain] = new() { Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = "pixeldrain" }
        };
        foreach (var (site, port) in new[] { (ThirdPartyId.PikPak, 8101), (ThirdPartyId.OneDrive, 8102), (ThirdPartyId.Pixeldrain, 8103) })
            Assert.AreEqual(port, new Uri(_engine.Start("rclone", _profile, ["lsjson", "managed:"], site).Environment["HTTPS_PROXY"]!).Port);
        _network.DownloadToolProxies = new() { [DownloadToolProxyTargets.Rclone] = new() { Mode = NetworkOptions.ProxyMode.DoNotUse } };
        var direct = _engine.Start("rclone", _profile, ["lsjson", "managed:"], ThirdPartyId.OneDrive);
        Assert.IsFalse(direct.Environment.ContainsKey("HTTPS_PROXY"));
        Assert.IsFalse(direct.Environment.ContainsKey("ALL_PROXY"));
    }

    [DataTestMethod]
    [DataRow("bad", "RCLONE_HASH_MISMATCH")]
    [DataRow("changed", "RCLONE_REMOTE_CHANGED")]
    [DataRow("failure", "RCLONE_REMOTE_FAILED")]
    public async Task FailedOrChangedDownloadsNeverPublishOrLeakUpstreamText(string mode, string code)
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        await File.WriteAllTextAsync(Path.Combine(_profile, "mode"), mode);
        var target = Path.Combine(_root, "destination");
        var error = await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.CopyAsync(_account, "folder", target, files, null, null, default));
        Assert.AreEqual(code, error.Message); Assert.IsFalse(Directory.Exists(target));
    }

    [TestMethod]
    public async Task CancellationWaitsForTheChildToExitAndDoesNotPublish()
    {
        var files = await _engine.ListAsync(_account, "folder", null, default);
        await File.WriteAllTextAsync(Path.Combine(_profile, "mode"), "cancel");
        using var cancel = new CancellationTokenSource();
        var target = Path.Combine(_root, "destination");
        var copy = _engine.CopyAsync(_account, "folder", target, files, null, null, cancel.Token);
        var pidPath = Path.Combine(_profile, "pid");
        for (var i = 0; i < 100 && !File.Exists(pidPath); i++) await Task.Delay(20);
        Assert.IsTrue(File.Exists(pidPath)); var pid = int.Parse(await File.ReadAllTextAsync(pidPath));
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => copy.WaitAsync(TimeSpan.FromSeconds(5)));
        try { using var process = Process.GetProcessById(pid); Assert.IsTrue(process.HasExited); }
        catch (ArgumentException) { }
        Assert.IsFalse(Directory.Exists(target));
    }

    [DataTestMethod]
    [DataRow("../outside")]
    [DataRow("//absolute")]
    [DataRow("x/../../outside")]
    [DataRow("x\\outside")]
    [DataRow("a\nb")]
    [DataRow(":local:/etc/passwd")]
    [DataRow("CON.txt")]
    [DataRow("a:ads")]
    public void RemotePathCannotEscapeOrInjectAConnectionString(string path) =>
        Assert.ThrowsExactly<RcloneRemoteException>(() => RcloneRemoteEngine.Remote(path));

    [TestMethod]
    public void ManifestRejectsCaseCollisionsAndFileDirectoryCollisions()
    {
        Assert.ThrowsExactly<RcloneRemoteException>(() => RcloneRemoteEngine.ValidateManifest([new("A", 1), new("a", 2)]));
        Assert.ThrowsExactly<RcloneRemoteException>(() => RcloneRemoteEngine.ValidateManifest([new("a", 1), new("a/file", 2)]));
    }

    [TestMethod]
    public async Task Configured_account_method_runs_the_actual_adapter_and_reuses_only_the_same_generation()
    {
        var (accounts, account, profile) = await AccountFixture();
        var provider = new RclonePikPakCloudProvider(_engine, accounts, new(Path.Combine(_root, "account-snapshots")));
        var registry = new DownloadMethodRegistry([provider], [], null!, null!, rcloneAccounts: accounts);
        var source = await registry.BindAccountAsync(DownloadSourceResolver.Normalize(new()
            {Id = "owned", Kind = "remote", ProviderId = "pikpak", Url = "/folder", MethodId = "pikpak.rclone", AccountId = account.Id}), null, default);
        var request = new CloudDownloadRequest("owned-operation", source, source.AccountId, null, Path.Combine(_root, "owned-output"));
        var result = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(result.Files.Single()));
        await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(profile, "copies.log")).Length);
        await accounts.SaveAsync(new() {Id = account.Id, ProviderId = "pikpak", Name = account.Name,
            Username = "different@example.invalid", Password = "replacement-fixture"}, default);
        await Assert.ThrowsExactlyAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("abc", await File.ReadAllTextAsync(result.Files.Single()));
    }

    [TestMethod]
    public async Task Account_adapter_restart_cannot_adopt_a_changed_remote_manifest_after_a_failed_copy()
    {
        var (accounts, account, profile) = await AccountFixture();
        var snapshots = Path.Combine(_root, "account-snapshots");
        var provider = new RclonePikPakCloudProvider(_engine, accounts, new(snapshots));
        var registry = new DownloadMethodRegistry([provider], [], null!, null!, rcloneAccounts: accounts);
        var source = await registry.BindAccountAsync(DownloadSourceResolver.Normalize(new()
            {Id = "owned", Kind = "remote", ProviderId = "pikpak", Url = "/folder", MethodId = "pikpak.rclone", AccountId = account.Id}), null, default);
        var request = new CloudDownloadRequest("owned-restart", source, source.AccountId, null, Path.Combine(_root, "owned-output"));
        await File.WriteAllTextAsync(Path.Combine(profile, "mode"), "failure");
        var failed = await Assert.ThrowsExactlyAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("RCLONE_REMOTE_FAILED", failed.Message);
        await File.WriteAllTextAsync(Path.Combine(profile, "mode"), "");
        await File.WriteAllTextAsync(Path.Combine(profile, "manifest.json"), Manifest.Replace("\"one\"", "\"two\""));
        provider = new(_engine, accounts, new(snapshots));
        var changed = await Assert.ThrowsExactlyAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("RCLONE_REMOTE_CHANGED", changed.Message);
        Assert.IsFalse(Directory.Exists(request.OutputDirectory));
        Assert.AreEqual(1, File.ReadAllLines(Path.Combine(profile, "copies.log")).Length);
    }

    private async Task<(RcloneAccountStore Accounts, RcloneAccountViewModel Account, string Profile)> AccountFixture()
    {
        var store = new RcloneAccountStore(Path.Combine(_root, "account-metadata"), new EngineProfiles(_engine));
        var account = await store.SaveAsync(new() {ProviderId = "pikpak", Name = "fixture",
            Username = "fixture@example.invalid", Password = "private-fixture"}, default);
        var profile = Path.Combine(_root, "private", "pikpak", account.Id);
        await File.WriteAllTextAsync(Path.Combine(profile, "manifest.json"), Manifest);
        return (store, account, profile);
    }

    private sealed class EngineProfiles(RcloneRemoteEngine engine) : IRcloneAccountProfiles
    {
        public Task<bool> HasAsync(RcloneProfileRef profile, CancellationToken ct) => engine.HasProfileAsync(profile, ct);
        public Task<string?> VersionAsync(RcloneProfileRef profile, CancellationToken ct) => engine.GetProfileVersionAsync(profile, ct);
        public Task RemoveAsync(RcloneProfileRef profile, CancellationToken ct) => engine.RemoveProfileAsync(profile, ct);
        public Task ConfigurePikPakAsync(string id, string username, string password, CancellationToken ct) => engine.ConfigurePikPakAsync(id, username, password, ct);
        public Task ConfigurePixeldrainAsync(string id, string root, string? key, CancellationToken ct) => throw new AssertFailedException("PikPak fixture only.");
        public Task ConfigureOneDriveAsync(string id, RcloneOneDriveConfiguration config, CancellationToken ct) => throw new AssertFailedException("PikPak fixture only.");
        public async Task ValidateAsync(RcloneProfileRef profile, CancellationToken ct) => _ = await engine.ListAsync(profile, "/", ct);
    }

    [TestMethod]
    public async Task SymlinkDestinationIsRejectedWithoutWritingOutside()
    {
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "link"); Directory.CreateSymbolicLink(link, outside);
        var files = await _engine.ListAsync(_account, "folder", null, default);
        await Assert.ThrowsExactlyAsync<RcloneRemoteException>(() =>
            _engine.CopyAsync(_account, "folder", link, files, null, null, default));
        Assert.AreEqual(0, Directory.GetFiles(outside).Length);
    }
}
