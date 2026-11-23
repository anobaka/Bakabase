using System.Reflection;
using System.Text.Json;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.TestKit.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
[DoNotParallelize]
public sealed class BaiduTransferConcurrencyTests
{
    [TestMethod]
    public async Task TransferUsesPinnedProfileAlongsideDownloadAndSerializesOnlyItsAccountsTransfers()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The native bridge fixture uses a Unix shell.");
        var root = Directory.CreateTempSubdirectory("baidu-transfer-").FullName;
        var anchor = typeof(AppService).GetField("_defaultAppDataDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = anchor.GetValue(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var downloadReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new List<Task>();
        try
        {
            anchor.SetValue(null, root);
            using var services = new ServiceCollection().BuildServiceProvider();
            var app = new AppService(NullLogger<AppService>.Instance, null!, services);
            Assert.AreEqual(root, app.AppDataDirectory);
            var dependency = new BaiduPcsGoService(NullLoggerFactory.Instance, app, null!, services);
            Directory.CreateDirectory(dependency.DefaultLocation);
            var binary = Path.Combine(dependency.DefaultLocation, "bakabase-baidu-pcs");
            await File.WriteAllTextAsync(binary, $$$"""
                #!/bin/sh
                if [ "$1" = "--version" ]; then echo '{{{BaiduPcsGoService.BridgeVersion}}}'; exit; fi
                IFS= read -r request
                case "$request" in
                    *'"operationId":"first"'*) operation=first ;;
                    *'"operationId":"second"'*) operation=second ;;
                    *'"operationId":"other"'*) operation=other ;;
                    *) exit 5 ;;
                esac
                printf '%s\n' "$request" > "$operation.tmp"
                mv "$operation.tmp" "$operation.request"
                if [ "$operation" = first ]; then
                    while [ ! -f release ]; do sleep 0.02; done
                fi
                echo '{"type":"result","receipt":{"remotePath":"/fixture","manifest":[]}}'
                """, ct);
            File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var bridge = new BaiduPcsBridgeClient(dependency, new(new TestBOptions<NetworkOptions>(new())));
            var store = new BaiduPcsAccountStore(app, bridge);
            var provider = new BaiduCloudDownloadProvider(store, bridge, dependency);
            var accountRoot = Directory.CreateDirectory(Path.Combine(root, "cloud-downloads", "baidu")).FullName;
            var primary = new BaiduPcsAccountViewModel(Guid.NewGuid().ToString("N"), "first", "123", true, true, null);
            var other = primary with {Id = Guid.NewGuid().ToString("N"), Uid = "456", IsDefault = false};
            var metadata = JsonSerializer.Serialize(new[] {primary, other}, JsonSerializerOptions.Web);
            await File.WriteAllTextAsync(Path.Combine(accountRoot, "accounts.json"), metadata, ct);
            foreach (var account in new[] {primary, other})
            {
                var path = Directory.CreateDirectory(Path.Combine(accountRoot, account.Id)).FullName;
                await File.WriteAllTextAsync(Path.Combine(path, "pcs_config.json"), "immutable profile fixture", ct);
            }
            var primaryPath = Path.Combine(accountRoot, primary.Id);
            Task<CloudTransferReceipt> Transfer(string operation, string id, CancellationToken token)
            {
                var request = new CloudTransferRequest(operation, new() {Id = "share", ProviderId = "baidu", Url = "https://example.invalid/share"}, id, "/fixture");
                var task = provider.TransferAsync(request, token);
                pending.Add(task);
                return task;
            }
            var downloadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var download = store.WithDownloadAccountAsync(primary.Id, (_, _) =>
            {
                downloadEntered.SetResult();
                return downloadReleased.Task.WaitAsync(ct);
            }, ct);
            pending.Add(download);
            await downloadEntered.Task.WaitAsync(ct);
            var first = Transfer("first", primary.Id, ct);
            var started = Path.Combine(primaryPath, "first.request");
            while (!File.Exists(started))
            {
                if (first.IsCompleted) await first;
                await Task.Delay(10, ct);
            }
            using var body = JsonDocument.Parse(await File.ReadAllTextAsync(started, ct));
            Assert.AreEqual("transfer", body.RootElement.GetProperty("command").GetString());
            Assert.AreEqual(primary.Uid, body.RootElement.GetProperty("expectedUid").GetString());
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var cancelled = Transfer("second", primary.Id, cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
            var second = Transfer("second", primary.Id, ct);
            var otherResult = await Transfer("other", other.Id, ct);
            Assert.AreEqual(other.Id, otherResult.Entry.AccountId);
            Assert.IsFalse(File.Exists(Path.Combine(primaryPath, "second.request")));
            downloadReleased.SetResult(true);
            await download;
            var mutation = store.WithAccountAsync(primary.Id, (_, _) => Task.FromResult(true), ct);
            pending.Add(mutation);
            Assert.IsFalse(mutation.IsCompleted, "An active transfer must exclude profile changes even after the download ends.");
            await File.WriteAllTextAsync(Path.Combine(primaryPath, "release"), "", ct);
            Assert.AreEqual(primary.Id, (await first).Entry.AccountId);
            Assert.IsTrue(await mutation);
            Assert.AreEqual(primary.Id, (await second).Entry.AccountId);
            Assert.AreEqual("immutable profile fixture", await File.ReadAllTextAsync(Path.Combine(primaryPath, "pcs_config.json"), ct));
        }
        finally
        {
            await timeout.CancelAsync();
            downloadReleased.TrySetResult(true);
            try { await Task.WhenAll(pending); } catch { /* Canceled or failed fixture processes must exit before cleanup. */ }
            anchor.SetValue(null, original);
            Directory.Delete(root, true);
        }
    }
}
