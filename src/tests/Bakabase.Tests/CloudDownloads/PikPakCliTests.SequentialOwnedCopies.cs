using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class PikPakCliTests
{
    [TestMethod]
    public async Task SecondShareRestoresOnlyAfterTheVerifiedFirstCopyStopsConsumingAccountSpace()
    {
        var cloud = new TwoShareCloud(_native);
        _accounts = new(Path.Combine(_root, "appdata"), cloud);
        _provider = new(_accounts, null, _ => Task.CompletedTask);
        _native.Continuation = """{"status":"success","scopes":["account","files:write","files:manage"]}""";
        var session = await _accounts.StartManageFilesAuthorizationAsync(default);
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        var account = await _accounts.GetAsync(default);
        await _accounts.SetCleanupSettingsAsync(new(account.AccountId!, account.AccountVersion!, true), default);

        var services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "db"), collection =>
        {
            collection.RemoveAll<ICloudDownloadProvider>();
            collection.AddSingleton<ICloudDownloadProvider>(_provider);
            collection.AddTransient(p => new BakabaseLocalizer(p.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        using var lifetime = (IDisposable)services;
        var db = services.GetRequiredService<BakabaseDbContext>();
        var copies = services.GetRequiredService<CloudDownloadOwnedCopyService>();
        var results = services.GetRequiredService<DownloadResultService>();

        async Task<(CloudDownloadRequest Request, DownloadTaskDbModel Task)> Register(string share)
        {
            var request = Request with
            {
                OperationId = "op-" + share, OutputDirectory = Path.Combine(_root, share),
                Source = Source with {Id = share, Kind = "share", Url = "https://mypikpak.com/s/" + share}
            };
            var task = new DownloadTaskDbModel
            {
                Key = share, ThirdPartyId = ThirdPartyId.PikPak, Type = 1, DownloadPath = request.OutputDirectory,
                Status = DownloadTaskDbModelStatus.InProgress, Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)
            };
            db.DownloadTasks.Add(task);
            await db.SaveChangesAsync();
            return (request, task);
        }

        async Task<CloudDownloadResult> Download((CloudDownloadRequest Request, DownloadTaskDbModel Task) item)
        {
            await copies.PrepareAsync(item.Task.Id, item.Request, _provider, default);
            return await _provider.DownloadAsync(item.Request, _ => Task.CompletedTask, default);
        }

        var first = await Register("first");
        var second = await Register("second");
        var downloaded = await Download(first);
        var waits = 0;
        var secondDownload = await PikPakCloudDownloader.RunWithCloudCapacityAsync(_provider, second.Request,
            () => Download(second), async () =>
            {
                CollectionAssert.AreEqual(new[] {"first"}, cloud.Restores);
                Assert.AreEqual(1, cloud.Containers.Count);
                switch (++waits)
                {
                    case 1:
                        Assert.IsFalse(await copies.ProcessNextAsync(default));
                        Assert.AreEqual(0, cloud.Trash.Count);
                        var receipt = await results.RecordFilesAsync(first.Task.Id, ThirdPartyId.PikPak, first.Task.Key,
                            first.Task.Key, first.Request.OutputDirectory, downloaded.Files.ToArray(), null);
                        await copies.DownloadVerifiedAsync(first.Task.Id, first.Request, receipt);
                        Assert.IsFalse(await copies.ProcessNextAsync(default));
                        await db.DownloadTasks.Where(t => t.Id == first.Task.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete));
                        Assert.IsTrue(await copies.ProcessNextAsync(default));
                        Assert.AreEqual("trashed", (await copies.GetAsync(first.Task.Id, default)).Status);
                        CollectionAssert.AreEquivalent(new[] {"copy-first"}, cloud.Trash.Keys.ToArray());
                        break;
                    case 2:
                        var capacity = await _accounts.GetCapacityAsync(account.AccountId, account.AccountVersion, default);
                        Assert.AreEqual(3m, capacity.TrashBytes!.Minimum);
                        Assert.AreEqual(2m, capacity.AvailableBytes!.Maximum);
                        // PikPak's retention expires between quota polls.
                        cloud.Trash.Remove("copy-first");
                        break;
                    default:
                        Assert.Fail("The second download should resume after PikPak reports sufficient space.");
                        break;
                }
            }, default, TimeSpan.Zero);

        Assert.AreEqual(2, waits);
        CollectionAssert.AreEqual(new[] {"first", "second"}, cloud.Restores);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(downloaded.Files.Single()));
        Assert.AreEqual("abc", await File.ReadAllTextAsync(secondDownload.Files.Single()));
        Assert.AreEqual("waitingDownload", (await copies.GetAsync(second.Task.Id, default)).Status);
    }

    // One account holds five bytes; the two remote originals each occupy three, even while in trash.
    private sealed class TwoShareCloud(Native cli) : IPikPakCliProcess
    {
        internal Dictionary<string, string> Containers { get; } = new();
        internal List<string> Restores { get; } = [];
        internal Dictionary<string, string> Trash { get; } = new();
        private readonly Dictionary<string, string> _inDrive = new();

        public async Task<JsonElement?> TryReadStatusAsync(string profile, CancellationToken ct) =>
            await RunAsync(profile, ["auth", "status"], ct);

        public async Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
            bool payload = false, Action? observeProgress = null)
        {
            var args = arguments.ToList();
            var parentIndex = args.IndexOf("--parent");
            var parent = parentIndex >= 0 ? args[parentIndex + 1] : null;
            switch (args[0])
            {
                case "auth" when args[1] == "status":
                    var used = 3 * (_inDrive.Count + Trash.Count);
                    return JsonSerializer.SerializeToElement(new {logged_in = true, user_id = "single-account",
                        storage = new {used = used + " B", total = "5 B", in_trash = (3 * Trash.Count) + " B"}});
                case "share" when args[1] == "get":
                    return JsonSerializer.SerializeToElement(new {status = "OK",
                        file_list = new[] {new {id = "shared-" + args[2], name = "original.bin", kind = "file", size = "3 B"}}});
                case "mkdir":
                    var id = "folder-" + (Containers.Count + 1);
                    Containers.Add(id, args[1]);
                    return JsonSerializer.SerializeToElement(new {id});
                case "share" when args[1] == "restore":
                    Assert.AreEqual(0, _inDrive.Count + Trash.Count, "Both copies cannot fit in this account.");
                    _inDrive.Add(parent!, args[2]);
                    Restores.Add(args[2]);
                    return JsonSerializer.SerializeToElement(new {restore_status = "COMPLETE", restore_task_id = "restore-" + args[2]});
                case "ls" when parent == null:
                    return JsonSerializer.SerializeToElement(new {files = Containers.Select(folder =>
                        new {id = folder.Key, name = folder.Value, kind = "drive#folder"})});
                case "ls":
                    var files = new List<object>();
                    if (_inDrive.TryGetValue(parent!, out var share))
                        files.Add(new {id = "copy-" + share, name = "original.bin", kind = "drive#file", size = "3",
                            hash = "remote-hash", modified_time = "2026-11-24T08:00:00Z", @params = new {original_share_id = share}});
                    return JsonSerializer.SerializeToElement(new {files});
                case "rm":
                    Assert.AreEqual(2, args.Count);
                    var restored = _inDrive.Single(entry => "copy-" + entry.Value == args[1]);
                    _inDrive.Remove(restored.Key);
                    Trash.Add(args[1], restored.Key);
                    return JsonSerializer.SerializeToElement(new {status = "accepted"});
                case "trash" when args[1] == "ls":
                    return JsonSerializer.SerializeToElement(new {files = Trash.Select(entry => new
                        {id = entry.Key, kind = "file", name = "original.bin", parent_id = entry.Value})});
                case "auth" when args[1] == "login":
                case "download":
                    return await cli.RunAsync(profile, arguments, ct, payload, observeProgress);
                default:
                    throw new AssertFailedException("Unexpected CLI command " + args[0]);
            }
        }
    }
}
