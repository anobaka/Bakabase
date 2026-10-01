using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
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

        protected override TransientRetry? GetTransientRetry(Exception e, int attempt) =>
            e is InvalidDataException or ExHentaiOriginalImageSafetyException || ExHentaiClient.IsImageNodeRecoveryExhausted(e)
                ? null : base.GetTransientRetry(e, attempt);


        protected static async Task<bool> CanReuseResultAsync(DownloadResultService results,
            Bakabase.InsideWorld.Business.Components.Downloader.Models.Db.DownloadResultDbModel result,
            bool preferTorrent, bool preferOriginal, CancellationToken ct)
        {
            if (result.Kind == DownloadResultKind.TorrentMetadata && (!preferTorrent ||
                ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result) == null) ||
                !await results.CanReuseAsync(result, ct)) return false;
            if (!preferOriginal || result.Kind != DownloadResultKind.LocalFiles) return true;
            try
            {
                var sourceFiles = JsonSerializer.Deserialize<string[]>(result.FilesJson) ?? [];
                return sourceFiles.Length > 0 && await results.ExHentaiLedger.HasPreferredImageResultAsync(
                    result.DownloadTaskId, result.SourceKey, sourceFiles, ct);
            }
            catch (JsonException) { return false; }
        }

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
            var canReusePrevious = previous != null &&
                await CanReuseResultAsync(results, previous, preferTorrent, preferOriginal, ct);
            if (canReusePrevious && previous != null)
            {
                // Reuse only actual source or placed files. A durable row alone cannot prove
                // completion after deletion; workflow placement remains a valid handoff.
                if (previous.Kind == DownloadResultKind.LocalFiles)
                {
                    var contents = await results.GetAvailableContentsAsync(previous, ct);
                    if (contents == null) throw new IOException("Recorded download files are missing.");
                    foreach (var file in contents.Files) await OnFileDownloadedInternal(file);
                }
                else if (previous.Kind == DownloadResultKind.TorrentMetadata)
                {
                    var torrentPath = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(previous)
                        ?? throw new IOException("The recorded torrent output path is invalid.");
                    // Restore a missing or damaged user copy from verified managed bytes.
                    // Bounded reads also protect recovery from a corrupt oversized file.
                    var metadata = await DownloadResultService.ReadReusableTorrentMetadataAsync(previous, ct)
                        ?? throw new IOException("The saved torrent metadata changed during recovery. Retry to acquire it again.");
                    var userCopyMatches = false;
                    try
                    {
                        await using var userCopy = File.OpenRead(torrentPath);
                        var userBytes = await Bakabase.Modules.Downloader.Components.TorrentMetadata.ReadBoundedAsync(userCopy, ct);
                        userCopyMatches = metadata.AsSpan().SequenceEqual(userBytes);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
                    if (!userCopyMatches)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(torrentPath)!);
                        ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(previous.DownloadDirectory, torrentPath);
                        var temporary = Path.Combine(Path.GetDirectoryName(torrentPath)!,
                            ".bakabase-torrent-" + Guid.NewGuid().ToString("N") + ".tmp");
                        var ownsTemporary = false;
                        try
                        {
                            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                ownsTemporary = true;
                                await output.WriteAsync(metadata, ct);
                            }
                            ct.ThrowIfCancellationRequested();
                            ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(previous.DownloadDirectory, torrentPath);
                            File.Move(temporary, torrentPath, true);
                        }
                        finally { if (ownsTemporary && File.Exists(temporary)) File.Delete(temporary); }
                    }
                    await OnFileDownloadedInternal(torrentPath);
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

            async Task<string> ResolveTorrentFileNameAsync()
            {
                var convention = GetEffectiveNamingConvention((await GetDownloaderOptionsAsync()).NamingConvention);
                var values = new Dictionary<ExHentaiNamingFields, object?>(baseNameSegmentsValues)
                {
                    [ExHentaiNamingFields.PageTitle] = betterName,
                    [ExHentaiNamingFields.Extension] = ".torrent"
                };
                var rendered = await BuildDownloadFilename(values);
                var templateParts = convention.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var renderedParts = rendered.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var galleryFields = new[] {"{RawName}", "{Name}", "{GalleryId}", "{GalleryToken}"};
                var fileTemplate = templateParts.Last();
                var fileNamesGallery = galleryFields.Any(field => fileTemplate.Contains(field, StringComparison.OrdinalIgnoreCase));
                // A page-oriented template puts the gallery's name in a directory. For a
                // torrent that component becomes the filename rather than another folder.
                var galleryNameParts = new List<string>();
                for (var index = 0; index < templateParts.Length - 1; index++)
                {
                    var part = templateParts[index];
                    if (fileNamesGallery &&
                        ((part.Equals("{RawName}", StringComparison.OrdinalIgnoreCase) &&
                          (fileTemplate.Contains("{RawName}", StringComparison.OrdinalIgnoreCase) ||
                           fileTemplate.Contains("{PageTitle}", StringComparison.OrdinalIgnoreCase))) ||
                         (part.Equals("{Name}", StringComparison.OrdinalIgnoreCase) &&
                          fileTemplate.Contains("{Name}", StringComparison.OrdinalIgnoreCase)))) continue;
                    if (galleryFields
                        .Any(field => part.Contains(field, StringComparison.OrdinalIgnoreCase)))
                        galleryNameParts.Add(renderedParts[index]);
                }
                var filename = Path.GetFileName(rendered);
                if (fileNamesGallery)
                    galleryNameParts.Add(filename.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
                        ? filename[..^".torrent".Length] : filename);
                if (galleryNameParts.Count > 0)
                    return ExHentaiTorrentFileName.Limit(
                        FileNameSanitizer.Sanitize(string.Join(" ", galleryNameParts) + ".torrent"), sourceKey);
                return ExHentaiTorrentFileName.Limit(
                    FileNameSanitizer.Sanitize(filename.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
                        ? filename : filename + ".torrent"), sourceKey);
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

                // Prefer a swarm with complete sources, then one with other active peers.
                // Within each group choose larger content; preserve window order for exact ties.
                var candidates = detail.Torrents
                    .OrderByDescending(t => t.Seeds > 0 ? 2 : t.Peers > 0 ? 1 : 0)
                    .ThenByDescending(t => t.Size)
                    .ThenByDescending(t => t.Seeds)
                    .ThenByDescending(t => t.Peers)
                    .ThenByDescending(t => t.UpdatedAt)
                    .ThenByDescending(t => t.Downloaded)
                    .ToList();

                // Automatic workflows already receive a dedicated work root; save-only
                // tasks write straight into the user's selected download directory.
                var path = Path.Combine(downloadPath, await ResolveTorrentFileNameAsync());
                if (resultWorkflowId.HasValue)
                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, downloadPath, path);
                if (resultWorkflowId.HasValue && !Directory.Exists(downloadPath))
                    Directory.CreateDirectory(downloadPath);
                ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(downloadPath, path);

                if (onCurrentChanged != null)
                {
                    await onCurrentChanged(Localizer["Downloader_ExHentai_DownloadingTorrent"]);
                }

                string? temporary = null;
                var candidateErrors = new List<Exception>();
                for (var index = 0; index < candidates.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var candidateTemporary = Path.Combine(downloadPath, $".bakabase-torrent-{Guid.NewGuid():N}.tmp");
                    var selected = false;
                    var validatingMetadata = false;
                    try
                    {
                        await Client.DownloadTorrent(candidates[index].DownloadUrl, candidateTemporary, ct);
                        // A candidate must also be usable by the torrent engine before it can
                        // replace the user's copy or create a durable download result.
                        await using (var metadataStream = File.OpenRead(candidateTemporary))
                        {
                            validatingMetadata = true;
                            var metadata = await Bakabase.Modules.Downloader.Components.TorrentMetadata
                                .ReadBoundedAsync(metadataStream, ct);
                            Bakabase.Modules.Downloader.Components.TorrentMetadata.Validate(metadata);
                        }
                        ct.ThrowIfCancellationRequested();
                        temporary = candidateTemporary;
                        selected = true;
                        break;
                    }
                    catch (Exception error) when (error is HttpRequestException or HttpIOException or
                                                   InvalidDataException or OperationCanceledException ||
                                                   error is ArgumentException && validatingMetadata)
                    {
                        // A caller's cancellation wins; a request timeout may try another link.
                        ct.ThrowIfCancellationRequested();
                        candidateErrors.Add(error);
                        if (index + 1 < candidates.Count)
                            Logger.LogWarning("Could not acquire ExHentai torrent candidate {Candidate} of {Total}; trying the next candidate.",
                                index + 1, candidates.Count);
                    }
                    finally
                    {
                        if (!selected && File.Exists(candidateTemporary)) File.Delete(candidateTemporary);
                    }
                }

                if (temporary == null)
                {
                    // Preserve every network failure for the existing transient-retry classifier.
                    // A single candidate retains its original exception type.
                    if (candidateErrors.Count == 1) ExceptionDispatchInfo.Capture(candidateErrors[0]).Throw();
                    throw new AggregateException("All ExHentai torrent candidates failed to download or validate.", candidateErrors);
                }

                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (resultWorkflowId.HasValue)
                        ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, downloadPath, path);
                    ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(downloadPath, path);
                    File.Move(temporary, path, true);
                    await OnFileDownloadedInternal(path);
                    // Persist the result only after the user's copy is in the selected directory.
                    // A failed move must remain retryable rather than looking completed.
                    if (resultWorkflowId.HasValue)
                        ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, downloadPath, path);
                    ExHentaiGalleryOutputPath.EnsureSafeTorrentOutputPath(downloadPath, path);
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

            bool CanConfirmFree(ExHentaiOriginalImageInfo info)
            {
                // A local clock alone cannot establish the site's free age/UTC window. The
                // client supplies a validated server Date advanced using monotonic elapsed time.
                return info.ServerTimeUtc is {Kind: DateTimeKind.Utc} serverNow &&
                       (DateTime.UtcNow - serverNow).Duration() <= TimeSpan.FromMinutes(2) &&
                       ExHentaiOriginalImagePolicy.IsPubliclyFree(detail.UpdateDt, serverNow);
            }

            ExHentaiImageDownloadOptions CreateOriginalDownloadOptions()
            {
                // Each attempt owns its preflight state; parallel pages must not overwrite
                // another page's free/paid decision or the limits checked before sending.
                var preflightWasFree = false;
                long preflightMinimum = 0, preflightMaximum = 0;

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

                return new ExHentaiImageDownloadOptions
                {
                    PreferOriginal = true,
                    RequestContext = requestContext,
                    BeforeOriginalDownload = BeforeOriginalDownload,
                    BeforeOriginalSend = BeforeOriginalSend,
                    CanRecoverOriginalWithoutGp = CanConfirmFree
                };
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
                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var batchToken = batchCts.Token;
                var imageTitleAndPageUrls = await Client.GetImageTitleAndPageUrlsFromDetailUrl(detail.Url, page, batchToken);

                var taskDataList = new List<(string filename, string pageUrl, string title)>();
                var options = await GetDownloaderOptionsAsync();

                foreach (var (title, pageUrl) in imageTitleAndPageUrls)
                {
                    batchToken.ThrowIfCancellationRequested();
                    // Inspect only this work's expected files. Reconstructing ownership from
                    // page titles also recovers downloads interrupted after writing a checkpoint.
                    checkpointContext.Analyze(title);
                    var keyFullname = await ResolvePagePath(title, Path.GetExtension(title));
                    batchToken.ThrowIfCancellationRequested();
                    var recordedImage = await ledger.GetImageAsync(downloadTaskId, sourceKey, pageUrl, batchToken);
                    string? existing = null;
                    if (recordedImage != null && (!preferOriginal || recordedImage.IsOriginal || recordedImage.OriginalUnavailable) && File.Exists(recordedImage.Path))
                    {
                        ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, recordedImage.Path);
                        existing = recordedImage.Path;
                    }
                    else if (!preferOriginal && File.Exists(keyFullname)) existing = keyFullname;
                    if (existing != null)
                    {
                        batchToken.ThrowIfCancellationRequested();
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
                    batchToken.ThrowIfCancellationRequested();
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCurrentChanged != null)
                {
                    batchToken.ThrowIfCancellationRequested();
                    await onCurrentChanged($"{doneCount}/{detail.FileCount}");
                }

                // Both original and displayed images use the configured concurrency. A failed
                // preflight cancels the batch, but already-sent requests may still incur charges.
                var threads = Math.Max(1, options.MaxConcurrency);
                using var sm = new SemaphoreSlim(threads, threads);
                var tasks = new ConcurrentQueue<Task>();
                ExceptionDispatchInfo? batchFailure = null;
                var batchCompleted = false;

                void RecordFailure(Exception error)
                {
                    // Cancellation of siblings is cleanup, not the cause of the failure.
                    if (error is OperationCanceledException && batchToken.IsCancellationRequested) return;
                    Interlocked.CompareExchange(ref batchFailure, ExceptionDispatchInfo.Capture(error), null);
                }

                async Task CancelBatch()
                {
                    try { await batchCts.CancelAsync(); }
                    catch (Exception error) { RecordFailure(error); }
                }

                // There is no need to save checkpoint during downloading files, because no extra request will be sent.
                // Although, the progress and current should be changed.
                var doneStates = new ConcurrentDictionary<string, bool>();

                var tmpCount = doneCount;
                var maxDoneCount = tmpCount + taskDataList.Count;

                async Task CurrentChanged()
                {
                    if (onCurrentChanged != null)
                    {
                        batchToken.ThrowIfCancellationRequested();
                        var d = tmpCount + doneStates.Count(a => a.Value);
                        var s = Math.Min(maxDoneCount, d + 1);
                        var e = Math.Min(maxDoneCount, d + tasks.Count(a => !a.IsCompleted));
                        var c = s == e ? s.ToString() : $"{s}-{e}";
                        await onCurrentChanged($"{c}/{detail.FileCount}");
                    }
                }

                try
                {
                    foreach (var (fullname, pageUrl, title) in taskDataList)
                    {
                        await sm.WaitAsync(batchToken);
                        async Task DownloadPage()
                        {
                            try
                            {
                                batchToken.ThrowIfCancellationRequested();
                                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, fullname);
                                Directory.CreateDirectory(Path.GetDirectoryName(fullname)!);
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
                                            r = await Client.DownloadImage(pageUrl, CreateOriginalDownloadOptions(), batchToken);
                                        }
                                        else r = await Client.DownloadImage(pageUrl, new ExHentaiImageDownloadOptions(), batchToken);
                                        data = r.Data;
                                        isOriginal = r.IsOriginal;
                                        originalUnavailable = r.OriginalUnavailable;
                                        break;
                                    }
                                    catch (Exception e) when (e is not InvalidDataException &&
                                                              e is not ExHentaiOriginalImageSafetyException &&
                                                              !ExHentaiClient.IsImageNodeRecoveryExhausted(e) &&
                                                              TransientNetworkError.IsTransient(e, batchToken))
                                    {
                                        // A cancelled download must fall straight through instead of
                                        // burning ten more attempts that are all guaranteed to fail.
                                        tryTimes++;
                                        if (tryTimes >= maxTryTimes)
                                        {
                                            throw;
                                        }

                                        // Access, quota and invalid image responses fail once. A transient
                                        // original retry still repeats both financial checks in the client.
                                        await Task.Delay(
                                            TransientNetworkError.GetBackoffDelay(tryTimes - 1,
                                                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)), batchToken);
                                    }
                                }

                                // Identify the container without decoding/re-encoding pixels. The
                                // thumbnail title may say .jpg while the server sends WebP or PNG.
                                batchToken.ThrowIfCancellationRequested();
                                var format = Image.DetectFormat(data);
                                var actualExtension = "." + format.FileExtensions.First();
                                var wrotePath = await ResolvePagePath(title, actualExtension);
                                batchToken.ThrowIfCancellationRequested();
                                Directory.CreateDirectory(Path.GetDirectoryName(wrotePath)!);
                                var temporary = Path.Combine(Path.GetDirectoryName(wrotePath)!, $".bakabase-image-{Guid.NewGuid():N}.tmp");
                                try
                                {
                                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, wrotePath);
                                    await File.WriteAllBytesAsync(temporary, data, batchToken);
                                    batchToken.ThrowIfCancellationRequested();
                                    ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, wrotePath);
                                    File.Move(temporary, wrotePath, true);
                                }
                                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                                await ledger.RecordImageAsync(downloadTaskId, sourceKey, pageUrl, wrotePath, isOriginal, batchToken, originalUnavailable);

                                batchToken.ThrowIfCancellationRequested();
                                workFiles[Path.GetFullPath(wrotePath)] = 0;
                                await OnFileDownloadedInternal(wrotePath);
                                batchToken.ThrowIfCancellationRequested();
                                doneStates[wrotePath] = true;
                                if (onProgress != null)
                                {
                                    await onProgress((tmpCount + doneStates.Count(a => a.Value)) * 100m / detail.FileCount);
                                }

                                await CurrentChanged();
                            }
                            catch (Exception error)
                            {
                                RecordFailure(error);
                                await CancelBatch();
                                throw;
                            }
                            finally
                            {
                                sm.Release();
                            }
                        }
                        // Do not pass the token to Task.Run: even a cancelled scheduled task must
                        // enter DownloadPage's finally to return the semaphore permit.
                        tasks.Enqueue(Task.Run(DownloadPage));
                    }

                    await Task.WhenAll(tasks);
                    batchCompleted = true;
                }
                catch (Exception error) { RecordFailure(error); }
                finally
                {
                    if (!batchCompleted) await CancelBatch();
                    // Scheduling errors and cancellation must also await every started worker.
                    // No worker may write a file or publish progress after this batch returns.
                    try { await Task.WhenAll(tasks); }
                    catch (Exception error) { RecordFailure(error); }
                }
                ct.ThrowIfCancellationRequested();
                batchFailure?.Throw();

                doneCount += taskDataList.Count;

                if (onProgress != null)
                {
                    batchToken.ThrowIfCancellationRequested();
                    await onProgress(doneCount * 100m / detail.FileCount);
                }

                if (onCheckpointChanged != null)
                {
                    // The final checkpoint follows result persistence below.
                    if (page < detail.PageCount - 1 && imageTitleAndPageUrls.Length > 0)
                    {
                        batchToken.ThrowIfCancellationRequested();
                        await onCheckpointChanged(checkpointContext.BuildCheckpoint(imageTitleAndPageUrls.Last().Title));
                    }
                }
            }

            if (doneCount != detail.FileCount || workFiles.Count != detail.FileCount)
                throw new InvalidDataException($"The gallery contains {detail.FileCount} images, but only {workFiles.Count} distinct files were downloaded. Retry to acquire the missing pages.");
            foreach (var file in workFiles.Keys)
            {
                ct.ThrowIfCancellationRequested();
                ExHentaiGalleryOutputPath.EnsureSafeOutputPath(configuredRoot, galleryDirectoryForImages, file);
            }
            await results.RecordFilesAsync(downloadTaskId, ThirdPartyId, sourceKey, betterName,
                downloadPath, workFiles.Keys.ToArray(), resultWorkflowId, ct);
            if (onCheckpointChanged != null)
            {
                ct.ThrowIfCancellationRequested();
                await onCheckpointChanged(checkpointContext.BuildCheckpointOnComplete());
            }
        }
    }
}
