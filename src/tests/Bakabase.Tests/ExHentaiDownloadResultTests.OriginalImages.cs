using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    private static void OriginalOptions(ExHentaiOptions options)
    {
        options.Cookie = "ipb_member_id=123; ipb_pass_hash=fixture; igneous=fixture";
        options.PreferOriginalImages = true;
        options.AllowOriginalImageGpSpending = false;
    }

    [TestMethod]
    public async Task OriginalImages_FreeGalleryPreservesBytesAndActualExtensionWithoutBalanceRequests()
    {
        var handler = new GalleryHandler
        {
            OriginalLinkSize = "1.00 MB", Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            ImageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")
        };
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        var result = (await _results.GetByTaskAsync(10)).Single();
        var file = JsonSerializer.Deserialize<string[]>(result.FilesJson)!.Single();
        Assert.AreEqual(".png", Path.GetExtension(file));
        CollectionAssert.AreEqual(handler.ImageBytes, await File.ReadAllBytesAsync(file));
        Assert.AreEqual(1, handler.OriginalRequests);
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, handler.BalanceRequests);
    }

    [TestMethod]
    public async Task OriginalImages_PaidSpendingDisabledStopsBeforeTheEndpoint()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1.00 MB", Posted = 1_600_000_000};
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(0, handler.OriginalRequests);
        Assert.AreEqual(0, handler.BalanceRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task OriginalImages_UnknownServerClockNeverAssumesFree()
    {
        var handler = new GalleryHandler
        {
            OriginalLinkSize = "1 MB", Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(), IncludeServerDate = false
        };
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(0, handler.OriginalRequests);
        Assert.AreEqual(0, handler.BalanceRequests);
    }

    [TestMethod]
    public async Task OriginalImages_TaskBudgetAlsoCoversTheNextGallery()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1 MB", Posted = 1_600_000_000};
        using var producer = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            options.OriginalImageMaximumGpCostPerTask = 1_000;
        });
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12346/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(1, handler.OriginalRequests);
    }

    [TestMethod]
    public async Task OriginalImages_AKnownServerErrorDoesNotRepeatPaidRequests()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1 MB", Posted = 1_600_000_000, OriginalReturnsHtml = true};
        using var producer = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
        });
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(1, handler.OriginalRequests);
        Assert.AreEqual(1, handler.BalanceRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task OriginalImages_BudgetSurvivesFailureRetryAndCanResumeOriginalPages()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1.00 MB", Posted = 1_600_000_000, ImagesPerPage = 2};
        using var producer = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            options.OriginalImageMaximumGpCostPerTask = 1_000;
        });
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(1, handler.OriginalRequests);
        Assert.AreEqual(1, Directory.GetFiles(_root, "*.jpg", SearchOption.AllDirectories).Length);
        // A different service/producer instance recovers both the reservation and the first original.
        using var retry = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            options.OriginalImageMaximumGpCostPerTask = 1_000;
        });
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(retry,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(1, handler.OriginalRequests, "A retry must neither re-charge page one nor reset the task's budget.");
        using var extended = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            options.OriginalImageMaximumGpCostPerTask = 2_000;
        });
        await RunProducer(extended, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(2, handler.OriginalRequests);
        Assert.AreEqual(2, JsonSerializer.Deserialize<string[]>((await _results.GetByTaskAsync(10)).Single().FilesJson)!.Length);
    }

    [DataTestMethod]
    [DataRow("<html>Please log in</html>", "1.00 MB", 10_000L)]
    [DataRow("Available: 10 kGP", "1.00 MB", 10_000L)]
    [DataRow("Available: 200 kGP", "size unknown", 10_000L)]
    public async Task OriginalImages_UnknownBalanceSizeOrInsufficientReserveStopsBeforeEndpoint(string balance,
        string size, long minimum)
    {
        var handler = new GalleryHandler {OriginalLinkSize = size, Posted = 1_600_000_000, BalanceHtml = balance};
        using var producer = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            options.OriginalImageMinimumGpBalance = minimum;
        });
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(0, handler.OriginalRequests);
    }

    [TestMethod]
    public async Task OriginalImages_DoNotTreatUnrecordedResampledFilesAsOriginals()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1 MB", Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()};
        var directory = Path.Combine(_root, "[Misc] Gallery 12345 [g12345]");
        Directory.CreateDirectory(directory);
        var existing = Path.Combine(directory, "001.jpg");
        await File.WriteAllTextAsync(existing, "old resampled image");
        using var producer = await BuildProducer(handler, configure: OriginalOptions);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(1, handler.OriginalRequests);
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(existing));
    }

    [TestMethod]
    public async Task Images_FormatMismatchPreservesResponseBytes()
    {
        var handler = new GalleryHandler {ImageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")};
        using var producer = await BuildProducer(handler);
        await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        var file = JsonSerializer.Deserialize<string[]>((await _results.GetByTaskAsync(10)).Single().FilesJson)!.Single();
        Assert.AreEqual(".png", Path.GetExtension(file));
        CollectionAssert.AreEqual(handler.GetImageBytes(), await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task OriginalImages_PermissionRevokedAfterPreflightStopsBeforeSending()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1 MB", Posted = 1_600_000_000};
        using var producer = await BuildProducer(handler, configure: options =>
        {
            OriginalOptions(options); options.AllowOriginalImageGpSpending = true;
            handler.BeforeOriginalSending = () => options.AllowOriginalImageGpSpending = false;
        });
        await Assert.ThrowsExceptionAsync<ExHentaiOriginalImageSafetyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false));
        Assert.AreEqual(0, handler.OriginalRequests);
        Assert.AreEqual(1, handler.BalanceRequests);
    }

    [TestMethod]
    public async Task OriginalImages_UpgradeCompletedNormalResultAndPreserveHistory()
    {
        var handler = new GalleryHandler {OriginalLinkSize = "1 MB", Posted = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()};
        using var normal = await BuildProducer(handler);
        await RunProducer(normal, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        var oldResult = (await _results.GetByTaskAsync(10)).Single();
        using var original = await BuildProducer(handler, configure: OriginalOptions);
        await RunProducer(original, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(1, handler.OriginalRequests, "A completed normal image result must not block an original pass.");
        Assert.IsTrue((await _results.GetByTaskAsync(10)).Any(x => x.Id == oldResult.Id));
        var requests = handler.Requests;
        await RunProducer(original, "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask, preferTorrent: false);
        Assert.AreEqual(requests, handler.Requests, "A completed original result remains idempotent.");
    }
}
