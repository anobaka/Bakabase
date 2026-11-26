using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class PikPakCliTests
{
    [TestMethod]
    public async Task NativeSiteRetiresOnlyItsReplacedCliPartialAfterCommittingTheHttpResult()
    {
        await Login();
        var request = Request with {Source = Source with {Kind = "share"}};
        _native.FailPayload = true;
        _native.KeepFailedNativePartial = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _provider.DownloadAsync(request, _ => Task.CompletedTask, default));

        var nativePart = _native.Calls.Single(call => call[0] == "download")[3] + ".part";
        Assert.AreEqual("bad", await File.ReadAllTextAsync(nativePart));
        var sibling = Path.Combine(Path.GetDirectoryName(nativePart)!, ".smbdeleteAAA59fc4.4");
        await File.WriteAllTextAsync(sibling, "unrelated file");
        _native.FailPayload = false;

        var services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "db"), collection =>
        {
            collection.RemoveAll<ICloudDownloadProvider>();
            collection.AddSingleton<ICloudDownloadProvider>(_provider);
            collection.AddTransient(p => new BakabaseLocalizer(p.GetRequiredService<IStringLocalizer<SharedResource>>()));
        });
        using var lifetime = (IDisposable)services;
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = new DownloadTaskDbModel
        {
            Key = "pikpak-share", ThirdPartyId = ThirdPartyId.PikPak, Type = (int)PikPakCloudTaskType.Share,
            DownloadPath = request.OutputDirectory, Status = DownloadTaskDbModelStatus.InProgress,
            Options = JsonSerializer.Serialize(request, JsonSerializerOptions.Web)
        };
        db.DownloadTasks.Add(task);
        await db.SaveChangesAsync();

        bool? partialPresentWhenReceiptCommitted = null;
        db.SavedChanges += (_, _) =>
        {
            if (partialPresentWhenReceiptCommitted == null && db.ChangeTracker.Entries<DownloadResultDbModel>()
                    .Any(entry => entry.Entity.DownloadTaskId == task.Id && entry.Entity.Id > 0))
                partialPresentWhenReceiptCommitted = File.Exists(nativePart);
        };
        var retry = new DownloadTask
        {
            Id = task.Id, Key = task.Key, ThirdPartyId = task.ThirdPartyId, Type = task.Type,
            Options = task.Options, DownloadPath = task.DownloadPath
        };
        retry.BindPreparedRetry(request.OperationId);
        using var site = new PikPakCloudDownloader(services);
        var finished = new TaskCompletionSource<DownloaderStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        site.OnStatusChanged += () =>
        {
            if (site.Status is DownloaderStatus.Complete or DownloaderStatus.Failed or DownloaderStatus.Stopped)
                finished.TrySetResult(site.Status);
            return Task.CompletedTask;
        };
        Assert.IsTrue(await site.Start(retry));
        Assert.AreEqual(DownloaderStatus.Complete, await finished.Task.WaitAsync(TimeSpan.FromSeconds(15)), site.Message);

        Assert.AreEqual(true, partialPresentWhenReceiptCommitted, "The abandoned partial must survive until the result commits.");
        var results = services.GetRequiredService<DownloadResultService>();
        var receipt = (await results.GetByTaskAsync(task.Id)).Single();
        var contents = await results.GetAvailableContentsAsync(receipt);
        Assert.IsNotNull(contents);
        var published = contents.Files.Single();
        Assert.AreEqual(Path.Combine(request.OutputDirectory, "folder", "file.bin"), published);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(published));
        Assert.IsFalse(File.Exists(nativePart), "Only the superseded CLI partial should be reclaimed.");
        Assert.AreEqual("unrelated file", await File.ReadAllTextAsync(sibling));
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual(1, _restored.Transfers.Count);
    }
}
