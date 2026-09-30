using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public abstract class AbstractExHentaiDownloader : AbstractDownloader<ExHentaiDownloadTaskType, ExHentaiTaskOptions>
    {
        protected readonly IStringLocalizer<SharedResource> Localizer;
        protected readonly ExHentaiClient Client;
        protected readonly ITextVocabularyService TextVocabularyService;
        protected readonly IHostEnvironment Env;
        // Serialize fullimg preflight + request across tasks; a balance read must not race
        // another in-app original request against the same account.
        private static readonly SemaphoreSlim OriginalImageGate = new(1, 1);
        
        protected AbstractExHentaiDownloader(IServiceProvider serviceProvider,
            IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ITextVocabularyService textVocabularyService,
            IHostEnvironment env) : base(serviceProvider)
        {
            Localizer = localizer;
            Client = client;
            TextVocabularyService = textVocabularyService;
            Env = env;
        }

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;


        protected async Task DownloadSingleWork(int downloadTaskId, string url, string checkpoint, string downloadPath,
            Func<string, Task> onNameAcquired,
            Func<string, Task> onCurrentChanged,
            Func<decimal, Task> onProgress,
            Func<string, Task> onCheckpointChanged,
            CancellationToken ct,
            bool preferTorrent = true,
            bool deferIfNoTorrent = false,
            Func<Task>? onNoTorrentDetected = null,
            Func<Task>? onTorrentDetected = null,
            Func<Task>? onTorrentDownloaded = null,
            int? resultWorkflowId = null)
        {
            var results = GetRequiredService<DownloadResultService>();
            var sourceKey = ExHentaiDownloadResultHelper.NormalizeSourceKey(url);
            var exOptionsManager = GetRequiredService<IBOptionsManager<ExHentaiOptions>>();
            var originalOptions = exOptionsManager.Value;
            var preferOriginal = originalOptions.PreferOriginalImages;
            var ledger = results.ExHentaiLedger;
            var previous = await results.GetLatestBySourceAsync(downloadTaskId, sourceKey, ct);
            var canReusePrevious = previous != null && (preferTorrent || previous.Kind != DownloadResultKind.TorrentMetadata);
            if (canReusePrevious && preferOriginal && previous!.Kind == DownloadResultKind.LocalFiles)
            {
                // Enabling originals must upgrade a completed resampled result too. Preserve
                // the old result's history, and reuse only outputs from an original-preference pass.
                var previousFiles = JsonSerializer.Deserialize<string[]>(previous.FilesJson) ?? [];
                canReusePrevious = await ledger.HasPreferredImageResultAsync(downloadTaskId, sourceKey, previousFiles, ct);
            }
            if (canReusePrevious && previous != null)
            {
                // The source work has already been durably handed off. A workflow retry must not
                // scrape the gallery again, even after the workflow moved its actual files.
                if (previous.Kind == DownloadResultKind.LocalFiles)
                {
                    try
                    {
                        foreach (var file in JsonSerializer.Deserialize<string[]>(previous.FilesJson) ?? [])
                        {
                            await OnFileDownloadedInternal(file);
                        }
                    }
                    catch (JsonException e)
                    {
                        Logger.LogWarning(e, "Could not read recorded files for download result {Id}", previous.Id);
                    }
                }
                else if (previous.Kind == DownloadResultKind.TorrentMetadata)
                {
                    // The result's FilesJson points to the managed metadata cache. Recover the
                    // user's original copy from the directory and filename used when it was saved.
                    var torrentFileName = FileNameSanitizer.Sanitize(
                        $"{previous.Name.RemoveInvalidFileNameChars()}.torrent");
                    await OnFileDownloadedInternal(Path.Combine(previous.DownloadDirectory, torrentFileName));
                }
                if (onNameAcquired != null) await onNameAcquired(previous.Name);
                if (previous.Kind == DownloadResultKind.TorrentMetadata && onTorrentDownloaded != null)
                    await onTorrentDownloaded();
                if (onProgress != null) await onProgress(100);
                if (onCheckpointChanged != null) await onCheckpointChanged("completed");
                return;
            }

            var configuredRoot = resultWorkflowId.HasValue
                ? Path.GetDirectoryName(Path.GetFullPath(downloadPath))!
                : downloadPath;
            Directory.CreateDirectory(configuredRoot);

            // Gallery metadata comes from the API; only fetch torrent download links when requested.
            var detail = await Client.ParseDetail(url, preferTorrent, ct);
            if (detail == null)
            {
                throw new Exception($"Got empty response from: {url}");
            }

            var betterName = detail.RawName.IsNullOrEmpty() ? detail.Name : detail.RawName;
            if (onNameAcquired != null)
            {
                await onNameAcquired(betterName);
            }

            var sourceKeyParts = sourceKey.Split('/');
            var baseNameSegmentsValues = new Dictionary<ExHentaiNamingFields, object?>
            {
                [ExHentaiNamingFields.GalleryId] = sourceKeyParts[0],
                [ExHentaiNamingFields.GalleryToken] = sourceKeyParts[1],
                [ExHentaiNamingFields.RawName] = detail.RawName,
                [ExHentaiNamingFields.Name] = detail.Name,
                [ExHentaiNamingFields.Category] = detail.Category,
            };

            // The template produces page paths. A flat template (or one with page fields in a
            // directory component) is wrapped in a stable gallery directory. The default template
            // includes the gallery ID; custom templates intentionally control their own uniqueness.
            async Task<(string Directory, string? TemplateDirectory)> ResolveGalleryDirectoryAsync()
            {
                var namingConvention = GetEffectiveNamingConvention((await GetDownloaderOptionsAsync()).NamingConvention);
                var templateDirectory = Path.GetDirectoryName(namingConvention);
                var pageDependentDirectory = templateDirectory?.Contains("{PageTitle}", StringComparison.OrdinalIgnoreCase) == true ||
                                             templateDirectory?.Contains("{Extension}", StringComparison.OrdinalIgnoreCase) == true;
                var sampleValues = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
                {
                    [ExHentaiNamingFields.PageTitle] = "bakabase-sample-page",
                    [ExHentaiNamingFields.Extension] = ".img"
                };
                var samplePath = await BuildDownloadFilename(sampleValues);
                var renderedDirectory = Path.GetDirectoryName(samplePath);
                var useTemplateDirectory = !pageDependentDirectory && !string.IsNullOrWhiteSpace(renderedDirectory);
                var relativeDirectory = useTemplateDirectory
                    ? renderedDirectory!
                    : FileNameSanitizer.Sanitize($"[{detail.Category}] {betterName}");
                if (string.IsNullOrWhiteSpace(relativeDirectory))
                    relativeDirectory = $"gallery-{sourceKeyParts[0]}";
                if (resultWorkflowId.HasValue && !Directory.Exists(downloadPath))
                {
                    // Automatic handoffs use a per-gallery work root beneath the configured
                    // download root. Create only that child, and only when this work has output.
                    var parent = Path.GetDirectoryName(Path.GetFullPath(downloadPath));
                    if (parent == null || !Directory.Exists(parent))
                        throw new DirectoryNotFoundException($"ExHentai download root is unavailable: {downloadPath}");
                    Directory.CreateDirectory(downloadPath);
                }
                return (ExHentaiGalleryOutputPath.Resolve(downloadPath, relativeDirectory),
                    useTemplateDirectory ? renderedDirectory : null);
            }

            // Use the API count: an empty torrent window must not become a cached negative verdict.
            if (preferTorrent && detail.TorrentCount > 0)
            {
                // Write the positive verdict down as soon as it is known, before the download that
                // may still fail: "this gallery has a torrent" is true either way, and it is what the
                // task list shows.
                if (onTorrentDetected != null)
                {
                    await onTorrentDetected();
                }

                if (detail.Torrents?.Any() != true)
                {
                    throw new Exception($"Gallery reports torrents but no download links were available: {url}");
                }

                // Select the best torrent (largest size, most recent)
                var bestTorrent = detail.Torrents
                    .OrderByDescending(t => t.Size)
                    .ThenByDescending(t => t.UpdatedAt)
                    .First();

                var (galleryDirectory, _) = await ResolveGalleryDirectoryAsync();
                var torrentFileName = FileNameSanitizer.Sanitize($"{betterName.RemoveInvalidFileNameChars()}.torrent");
                var path = Path.Combine(galleryDirectory, torrentFileName);
                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectory, path);

                if (onCurrentChanged != null)
                {
                    await onCurrentChanged(Localizer["Downloader_ExHentai_DownloadingTorrent"]);
                }

                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await Client.DownloadTorrent(bestTorrent.DownloadUrl, temporary, ct);
                    // Verify before replacing the user's copy. An HTTP-200 error page or metadata
                    // rejected by the engine must never become a cached .torrent on disk.
                    await using (var metadataStream = File.OpenRead(temporary))
                    {
                        var metadata = await Bakabase.Modules.Downloader.Components.TorrentMetadata
                            .ReadBoundedAsync(metadataStream, ct);
                        Bakabase.Modules.Downloader.Components.TorrentMetadata.Validate(metadata);
                    }
                    ct.ThrowIfCancellationRequested();
                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectory, path);
                    File.Move(temporary, path, true);
                    await OnFileDownloadedInternal(path);
                    // Persist the result only after the user's copy is in the selected directory.
                    // A failed move must remain retryable rather than looking completed.
                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectory, path);
                    await results.RecordTorrentAsync(downloadTaskId, ThirdPartyId, sourceKey, betterName,
                        path, resultWorkflowId, ct);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }

                // Only now — the file is on disk. This is the stamp that lets a later run skip the
                // task without touching the network or the folder, so it must not be written
                // anywhere a failure could still reach it.
                if (onTorrentDownloaded != null)
                {
                    await onTorrentDownloaded();
                }

                if (onProgress != null)
                {
                    await onProgress(100);
                }

                if (onCheckpointChanged != null)
                {
                    await onCheckpointChanged("completed");
                }

                return;
            }

            // Reached here => this gallery has no torrent. Record that whenever we actually probed,
            // not just on the deferring path: the verdict is what lets a later run skip the probe,
            // and it is equally true when torrent-priority is off.
            if (preferTorrent && onNoTorrentDetected != null)
            {
                await onNoTorrentDetected();
            }

            // Under torrent-priority, yield the slot back to the queue so torrent-bearing tasks are
            // drained first. The deferred task is re-selected only once no un-probed task remains, at
            // which point this method is called again with deferIfNoTorrent=false and downloads images.
            if (deferIfNoTorrent)
            {
                throw new DownloadDeferredException();
            }

            var (galleryDirectoryForImages, stableTemplateDirectory) = await ResolveGalleryDirectoryAsync();

            //var limit = await _client.GetImageLimits();
            //if (limit.Rest <= imageTitleAndPageUrls.Length)
            //{
            //    throw new Exception(
            //        $"Image limits reached, {limit.Current}/{limit.Limit}, needs {imageTitleAndPageUrls.Length}");
            //}

            var checkpointContext = new RangeCheckpointContext(checkpoint);
            var workFiles = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var doneCount = 0;
            var cookieSnapshot = originalOptions.Cookie;
            var requestContext = preferOriginal ? new ExHentaiRequestContext(cookieSnapshot) : null;
            var preflightWasFree = false;
            long preflightMinimum = 0, preflightMaximum = 0;

            bool CanConfirmFree(ExHentaiOriginalImageInfo info)
            {
                // A local clock alone cannot establish the site's free age/UTC window. The
                // client supplies a validated server Date advanced using monotonic elapsed time.
                return info.ServerTimeUtc is {Kind: DateTimeKind.Utc} serverNow &&
                       (DateTime.UtcNow - serverNow).Duration() <= TimeSpan.FromMinutes(2) &&
                       ExHentaiOriginalImagePolicy.IsPubliclyFree(detail.UpdateDt, serverNow);
            }

            async Task BeforeOriginalDownload(ExHentaiOriginalImageInfo info, CancellationToken token)
            {
                var current = exOptionsManager.Value;
                if (!current.PreferOriginalImages || !string.Equals(current.Cookie, cookieSnapshot, StringComparison.Ordinal))
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the account or original-image preference changed. Retry to apply the new settings.");
                preflightWasFree = CanConfirmFree(info);
                if (preflightWasFree) return;
                if (!current.AllowOriginalImageGpSpending)
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: this request could consume GP/Credits and spending is disabled. Previously downloaded files are retained. No paid original-image request was sent.");
                var reservation = ExHentaiOriginalImagePolicy.EstimateGpReservation(info.OriginalSizeBytes);
                var minimum = current.OriginalImageMinimumGpBalance ?? ExHentaiOriginalImagePolicy.DefaultMinimumGpBalance;
                var maximum = current.OriginalImageMaximumGpCostPerTask ?? ExHentaiOriginalImagePolicy.DefaultMaximumGpCostPerTask;
                if (minimum < 0 || maximum < 0)
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: GP limits must be non-negative.");
                try
                {
                    var balance = await Client.GetAccountBalance(requestContext!, token);
                    ExHentaiOriginalImagePolicy.CheckBalance(balance.GpBalance, reservation, minimum);
                    await ledger.ReserveGpAsync(downloadTaskId, reservation, maximum, token);
                    preflightMinimum = minimum;
                    preflightMaximum = maximum;
                }
                catch (Exception e) when (e is not ExHentaiOriginalImageSafetyException && e is not OperationCanceledException)
                {
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the account balance or durable GP budget could not be verified. No paid original-image request was sent. " + e.Message);
                }
            }

            Task BeforeOriginalSend(ExHentaiOriginalImageInfo info, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var current = exOptionsManager.Value;
                if (!current.PreferOriginalImages || !string.Equals(current.Cookie, cookieSnapshot, StringComparison.Ordinal))
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the account or original-image preference changed while the request was queued.");
                if (preflightWasFree)
                {
                    if (!CanConfirmFree(info))
                        throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: the free-download window ended while the request was queued. Retry to apply the current spending policy. No paid original-image request was sent.");
                }
                else if (!current.AllowOriginalImageGpSpending ||
                         (current.OriginalImageMinimumGpBalance ?? ExHentaiOriginalImagePolicy.DefaultMinimumGpBalance) > preflightMinimum ||
                         (current.OriginalImageMaximumGpCostPerTask ?? ExHentaiOriginalImagePolicy.DefaultMaximumGpCostPerTask) < preflightMaximum)
                    throw new ExHentaiOriginalImageSafetyException("Original-image download stopped: GP permission or limits changed while the request was queued. Retry to apply the new limits.");
                return Task.CompletedTask;
            }

            async Task<string> ResolvePagePath(string title, string extension)
            {
                var values = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
                {
                    [ExHentaiNamingFields.PageTitle] = Path.GetFileNameWithoutExtension(title),
                    [ExHentaiNamingFields.Extension] = extension
                };
                var filename = await BuildDownloadFilename(values);
                var relative = stableTemplateDirectory == null ? filename :
                    string.Equals(Path.GetDirectoryName(filename), stableTemplateDirectory, StringComparison.Ordinal)
                        ? Path.GetFileName(filename)
                        : throw new IOException("The gallery directory changed while downloading. Retry with a stable naming convention.");
                var path = Path.GetFullPath(Path.Combine(galleryDirectoryForImages, relative));
                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, path);
                return path;
            }

            // API filecount counts images, while this count depends on the user's thumbnail
            // layout. Read it only when downloading images, after a torrent-only pass can yield.
            detail.PageCount = await Client.GetGalleryPageCount(detail.Url, ct);

            for (var page = 0; page < detail.PageCount; page++)
            {
                var imageTitleAndPageUrls = await Client.GetImageTitleAndPageUrlsFromDetailUrl(detail.Url, page, ct);

                var taskDataList = new List<(string filename, string pageUrl, string title)>();
                var options = await GetDownloaderOptionsAsync();

                foreach (var (title, pageUrl) in imageTitleAndPageUrls)
                {
                    // Inspect only this work's expected files. Reconstructing ownership from
                    // page titles also recovers downloads interrupted after writing a checkpoint.
                    checkpointContext.Analyze(title);
                    var keyFullname = await ResolvePagePath(title, Path.GetExtension(title));
                    var recordedImage = await ledger.GetImageAsync(downloadTaskId, sourceKey, pageUrl, ct);
                    string? existing = null;
                    if (recordedImage != null && (!preferOriginal || recordedImage.IsOriginal || recordedImage.OriginalUnavailable) && File.Exists(recordedImage.Path))
                    {
                        ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, recordedImage.Path);
                        existing = recordedImage.Path;
                    }
                    else if (!preferOriginal && File.Exists(keyFullname)) existing = keyFullname;
                    if (existing != null)
                    {
                        workFiles[existing] = 0;
                        await OnFileDownloadedInternal(existing);
                        doneCount++;
                    }
                    else
                    {
                        taskDataList.Add((keyFullname, pageUrl, title));
                    }
                }

                if (onProgress != null)
                {
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCurrentChanged != null)
                {
                    await onCurrentChanged($"{doneCount}/{detail.FileCount}");
                }

                // Avoid large mount of tasks being created.
                // Original downloads stop on the first financial preflight failure. Keep their
                // requests sequential so later pages cannot spend while a failed page unwinds.
                var threads = preferOriginal ? 1 : Math.Max(1, options.MaxConcurrency);
                var sm = new SemaphoreSlim(threads, threads);
                var tasks = new ConcurrentBag<Task>();

                // There is no need to save checkpoint during downloading files, because no extra request will be sent.
                // Although, the progress and current should be changed.
                var doneStates = new ConcurrentDictionary<string, bool>();

                var tmpCount = doneCount;
                var maxDoneCount = tmpCount + taskDataList.Count;

                async Task CurrentChanged()
                {
                    if (onCurrentChanged != null)
                    {
                        var d = tmpCount + doneStates.Count(a => a.Value);
                        var s = Math.Min(maxDoneCount, d + 1);
                        var e = Math.Min(maxDoneCount, d + tasks.Count(a => !a.IsCompleted));
                        var c = s == e ? s.ToString() : $"{s}-{e}";
                        await onCurrentChanged($"{c}/{detail.FileCount}");
                    }
                }

                foreach (var (fullname, pageUrl, title) in taskDataList)
                {
                    var dir = Path.GetDirectoryName(fullname)!;
                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages,
                        fullname);
                    Directory.CreateDirectory(dir);

                    // Give up once a run of downloads has all genuinely failed — a banned IP or an
                    // expired cookie fails every image, and grinding through the whole gallery to
                    // learn that wastes the request budget it takes to find out.
                    const int continuousFailedTaskSampleCount = 10;
                    var recent = tasks.TakeLast(continuousFailedTaskSampleCount).ToArray();

                    if (recent.Length == continuousFailedTaskSampleCount && recent.All(x => x.IsFaulted))
                    {
                        // Was "!IsCompletedSuccessfully", which is also true of a task that is merely
                        // still running — so a slow batch tripped the check and then threw a
                        // NullReferenceException off the null Exception of an unfinished task,
                        // reporting a crash instead of the download error that never happened.
                        throw recent.Last().Exception!;
                    }

                    await sm.WaitAsync(ct);
                    async Task DownloadPage()
                    {
                        try
                        {
                            await CurrentChanged();

                            const int maxTryTimes = 10;
                            var tryTimes = 0;
                            byte[] data;
                            var isOriginal = false;
                            var originalUnavailable = false;
                            while (true)
                            {
                                try
                                {
                                    ExHentaiDownloadedImage r;
                                    if (preferOriginal)
                                    {
                                        await OriginalImageGate.WaitAsync(ct);
                                        try
                                        {
                                            r = await Client.DownloadImage(pageUrl, new ExHentaiImageDownloadOptions
                                            {
                                                PreferOriginal = true,
                                                RequestContext = requestContext,
                                                BeforeOriginalDownload = BeforeOriginalDownload,
                                                BeforeOriginalSend = BeforeOriginalSend
                                            }, ct);
                                        }
                                        finally { OriginalImageGate.Release(); }
                                    }
                                    else r = await Client.DownloadImage(pageUrl, new ExHentaiImageDownloadOptions(), ct);
                                    data = r.Data;
                                    isOriginal = r.IsOriginal;
                                    originalUnavailable = r.OriginalUnavailable;
                                    break;
                                }
                                catch (Exception e) when (!ct.IsCancellationRequested && e is not ExHentaiOriginalImageSafetyException &&
                                                          (!preferOriginal || TransientNetworkError.IsTransient(e, ct)))
                                {
                                    // A cancelled download must fall straight through instead of
                                    // burning ten more attempts that are all guaranteed to fail.
                                    tryTimes++;
                                    if (tryTimes >= maxTryTimes)
                                    {
                                        throw;
                                    }

                                    // A known access/quota/format error must not repeat a potentially
                                    // paid original request. Only transient failures retry originals.
                                    // A dropped connection or a TLS handshake cut short by a flaky image
                                    // server usually needs a moment, not an instant re-dial: back to back,
                                    // the ten attempts were all spent within the first seconds of a brief
                                    // outage. Other failures keep retrying at the request pace as before.
                                    if (TransientNetworkError.IsTransient(e, ct))
                                    {
                                        await Task.Delay(
                                            TransientNetworkError.GetBackoffDelay(tryTimes - 1,
                                                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)), ct);
                                    }
                                }
                            }

                            // Identify the container without decoding/re-encoding pixels. The
                            // thumbnail title may say .jpg while the server sends WebP or PNG.
                            var format = Image.DetectFormat(data);
                            var actualExtension = "." + format.FileExtensions.First();
                            var wrotePath = await ResolvePagePath(title, actualExtension);
                            Directory.CreateDirectory(Path.GetDirectoryName(wrotePath)!);
                            var temporary = wrotePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, wrotePath);
                                await File.WriteAllBytesAsync(temporary, data, ct);
                                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, wrotePath);
                                File.Move(temporary, wrotePath, true);
                            }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                            await ledger.RecordImageAsync(downloadTaskId, sourceKey, pageUrl, wrotePath, isOriginal, ct, originalUnavailable);

                            workFiles[Path.GetFullPath(wrotePath)] = 0;
                            await OnFileDownloadedInternal(wrotePath);
                            doneStates[wrotePath] = true;
                            if (onProgress != null)
                            {
                                await onProgress((tmpCount + doneStates.Count(a => a.Value)) * 100m / detail.FileCount);
                            }

                            await CurrentChanged();
                        }
                        finally
                        {
                            sm.Release();
                        }
                    }
                    if (preferOriginal) await DownloadPage();
                    else tasks.Add(Task.Run(DownloadPage));
                }

                await Task.WhenAll(tasks);

                doneCount += taskDataList.Count;

                if (onProgress != null)
                {
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCheckpointChanged != null)
                {
                    // The final checkpoint follows result persistence below.
                    if (page < detail.PageCount - 1 && imageTitleAndPageUrls.Length > 0)
                        await onCheckpointChanged(checkpointContext.BuildCheckpoint(imageTitleAndPageUrls.Last().Title));
                }
            }

            foreach (var file in workFiles.Keys)
                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, file);
            await results.RecordFilesAsync(downloadTaskId, ThirdPartyId, sourceKey, betterName,
                downloadPath, workFiles.Keys.ToArray(), resultWorkflowId, ct);
            if (onCheckpointChanged != null)
                await onCheckpointChanged(checkpointContext.BuildCheckpointOnComplete());
        }
    }
}
