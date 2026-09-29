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
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
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
            var previous = await results.GetLatestBySourceAsync(downloadTaskId, sourceKey, ct);
            if (previous != null)
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

            // Only fetch torrent info when preferTorrent is true
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

            // Check if torrents are available and download torrent instead of images
            if (detail.Torrents?.Any() == true)
            {
                // Write the positive verdict down as soon as it is known, before the download that
                // may still fail: "this gallery has a torrent" is true either way, and it is what the
                // task list shows.
                if (onTorrentDetected != null)
                {
                    await onTorrentDetected();
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

            for (var page = 0; page < detail.PageCount; page++)
            {
                var imageTitleAndPageUrls = await Client.GetImageTitleAndPageUrlsFromDetailUrl(detail.Url, page, ct);

                var taskDataList = new List<(string filename, string pageUrl)>();
                var options = await GetDownloaderOptionsAsync();

                foreach (var (title, pageUrl) in imageTitleAndPageUrls)
                {
                    // Inspect only this work's expected files. Reconstructing ownership from
                    // page titles also recovers downloads interrupted after writing a checkpoint.
                    checkpointContext.Analyze(title);
                    var extension = Path.GetExtension(title);
                    var fullNameSegmentsValues = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
                    {
                        [ExHentaiNamingFields.PageTitle] = Path.GetFileNameWithoutExtension(title),
                        [ExHentaiNamingFields.Extension] = extension
                    };
                    var keyFilename = await BuildDownloadFilename(fullNameSegmentsValues);
                    var relativeFile = stableTemplateDirectory == null
                        ? keyFilename
                        : string.Equals(Path.GetDirectoryName(keyFilename), stableTemplateDirectory,
                            StringComparison.Ordinal)
                            ? Path.GetFileName(keyFilename)
                            : throw new IOException("The gallery directory changed while downloading. Retry with a stable naming convention.");
                    var keyFullname = Path.GetFullPath(Path.Combine(galleryDirectoryForImages, relativeFile));
                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages,
                        keyFullname);
                    if (File.Exists(keyFullname))
                    {
                        workFiles[keyFullname] = 0;
                        await OnFileDownloadedInternal(keyFullname);
                        doneCount++;
                    }
                    else
                    {
                        taskDataList.Add((keyFullname, pageUrl));
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
                var threads = options.MaxConcurrency;
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

                foreach (var (fullname, pageUrl) in taskDataList)
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
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await CurrentChanged();

                            const int maxTryTimes = 10;
                            var tryTimes = 0;
                            byte[] data;
                            string? contentType = null;
                            while (true)
                            {
                                try
                                {
                                    var r = await Client.DownloadImage(pageUrl, ct);
                                    data = r.Data;
                                    contentType = r.ContentType;
                                    break;
                                }
                                catch (Exception e) when (!ct.IsCancellationRequested)
                                {
                                    // A cancelled download must fall straight through instead of
                                    // burning ten more attempts that are all guaranteed to fail.
                                    tryTimes++;
                                    if (tryTimes >= maxTryTimes)
                                    {
                                        throw;
                                    }

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

                            string MapContentTypeToExtension(string? ict)
                            {
                                return ict?.ToLowerInvariant() switch
                                {
                                    "image/jpeg" => ".jpg",
                                    "image/jpg" => ".jpg",
                                    "image/png" => ".png",
                                    "image/webp" => ".webp",
                                    "image/gif" => ".gif",
                                    "image/bmp" => ".bmp",
                                    "image/tiff" => ".tiff",
                                    _ => string.Empty
                                };
                            }

                            var targetExt = Path.GetExtension(fullname);
                            var actualExt = MapContentTypeToExtension(contentType);

                            var wrotePath = fullname;
                            var wrote = false;

                            if (actualExt.IsNullOrEmpty() || string.Equals(actualExt, targetExt, StringComparison.OrdinalIgnoreCase))
                            {
                                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot,
                                    galleryDirectoryForImages, fullname);
                                await File.WriteAllBytesAsync(fullname, data, ct);
                                wrote = true;
                            }
                            else
                            {
                                // Try convert to target format indicated by title
                                try
                                {
                                    using var image = Image.Load(data);
                                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot,
                                        galleryDirectoryForImages, fullname);
                                    await image.SaveAsync(fullname, ct);
                                    wrote = true;
                                }
                                catch
                                {
                                    // ignore conversion failure
                                }

                                if (!wrote)
                                {
                                    // Fallback: save using actual format extension
                                    var dirName = Path.GetDirectoryName(fullname)!;
                                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullname);
                                    var actualFullName = Path.Combine(dirName, fileNameWithoutExt + actualExt);
                                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot,
                                        galleryDirectoryForImages, actualFullName);
                                    await File.WriteAllBytesAsync(actualFullName, data, ct);
                                    wrote = true;
                                    wrotePath = actualFullName;
                                }
                            }

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
                    }, ct));
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
