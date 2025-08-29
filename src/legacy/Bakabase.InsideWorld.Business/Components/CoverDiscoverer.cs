using System.IO;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using ImageHelpers = Bakabase.Abstractions.Helpers.ImageHelpers;

namespace Bakabase.InsideWorld.Business.Components;

public class CoverDiscoverer(ILoggerFactory loggerFactory, FfMpegService ffMpegService) : ICoverDiscoverer
{
    private readonly ILogger<CoverDiscoverer> _logger = loggerFactory.CreateLogger<CoverDiscoverer>();
    private static readonly SemaphoreSlim FindCoverInVideoSm = new SemaphoreSlim(2, 2);

    public async Task<CoverDiscoveryResult?> Discover(string path, CoverSelectOrder order, bool useIconAsFallback,
        CancellationToken ct)
    {
        var imageExtensions = InternalOptions.ImageExtensions;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        var attr = File.GetAttributes(path);
        var files = attr.HasFlag(FileAttributes.Directory)
            ? new DirectoryInfo(path).GetFiles("*.*", SearchOption.AllDirectories)
            : new[] {new FileInfo(path)};
        switch (order)
        {
            case CoverSelectOrder.FileModifyDtDescending:
                files = files.OrderByDescending(a => a.LastWriteTime).ToArray();
                break;
            case CoverSelectOrder.FilenameAscending:
            default:
                break;
        }

        // Find cover.{ext}
        var coverImg = files.FirstOrDefault(t =>
            imageExtensions.Any(e => t.Name.Equals($"cover{e}", StringComparison.OrdinalIgnoreCase)));
        if (coverImg != null)
        {
            return new CoverDiscoveryResult(false, _buildCoverPath(coverImg.FullName),
                Path.GetExtension(coverImg.FullName));
        }

        // Find first image
        var firstImage = files.FirstOrDefault(a => imageExtensions.Contains(a.Extension));
        if (firstImage != null)
        {
            return new CoverDiscoveryResult(false, _buildCoverPath(firstImage.FullName),
                Path.GetExtension(firstImage.FullName));
        }

        // additional sources
        foreach (var @as in SpecificEnumUtils<AdditionalCoverDiscoveringSource>.Values)
        {
            switch (@as)
            {
                case AdditionalCoverDiscoveringSource.Video:
                {
                    if (ffMpegService.Status != DependentComponentStatus.Installed)
                    {
                        continue;
                    }

                    var firstVideoFile = files.FirstOrDefault(t =>
                        InternalOptions.VideoExtensions.Contains(Path.GetExtension(t.Name)) && t.Length > 0);
                    if (firstVideoFile == null)
                    {
                        break;
                    }

                    await FindCoverInVideoSm.WaitAsync(ct);
                    var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try
                    {
                        var mixedCt = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
                        var durationSeconds = await ffMpegService.GetDuration(firstVideoFile.FullName, mixedCt.Token);
                        var duration = TimeSpan.FromSeconds(durationSeconds);
                        var screenshotTime = duration * 0.2;
                        var ms = await ffMpegService.CaptureFrame(firstVideoFile.FullName, screenshotTime, mixedCt.Token);
                        const string ext = ".jpg";
                        return new CoverDiscoveryResult(true,
                            _buildCoverPath(firstVideoFile.FullName,
                                $"{screenshotTime.ToString("g").Replace(':', '.')}{ext}"), ext, ms.ToArray());
                    }
                    catch (OperationCanceledException tce)
                    {
                        if (timeoutCts.Token.IsCancellationRequested)
                        {
                            _logger.LogWarning(
                                $"Timeout occurred during capture a frame from video file {firstVideoFile.FullName}.");
                        }
                        else
                        {
                            _logger.LogError(tce, "An error occurred during capture a frame from video file");
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "An error occurred during capture a frame from video file");
                    }
                    finally
                    {
                        FindCoverInVideoSm.Release();
                    }

                    break;
                }
                case AdditionalCoverDiscoveringSource.CompressedFile:
                {
                    // todo: catch exception
                    const int maxTryTimes = 1;
                    var tryTimes = 0;
                    for (var i = 0; i < files.Length && tryTimes < maxTryTimes; i++)
                    {
                        var file = files[i];
                        if (file.Length > 0)
                        {
                            if (InternalOptions.CompressedFileExtensions.Contains(file.Extension))
                            {
                                try
                                {
                                    MemoryStream? imageStream = null;
                                    string? key = null;

                                    if (InternalOptions.SevenZipCompressedFileExtension.Equals(file.Extension,
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        var archive = SevenZipArchive.Open(file.FullName);
                                        var imageFile = archive.Entries
                                            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                                            .FirstOrDefault(a =>
                                                imageExtensions.Contains(Path.GetExtension(a.Key)));
                                        if (imageFile != null)
                                        {
                                            key = imageFile.Key;
                                            await using var s = imageFile.OpenEntryStream();
                                            await s.CopyToAsync(imageStream = new MemoryStream(), ct);
                                            imageStream.Seek(0, SeekOrigin.Begin);
                                        }
                                    }
                                    else
                                    {
                                        string firstFileKey = null;
                                        await using (Stream stream = file.OpenRead())
                                        {
                                            // reader.MoveToNextEntry will be broken after reader.OpenEntryStream being called,
                                            // so we do not store entry stream there.
                                            using (var reader = ReaderFactory.Open(stream))
                                            {
                                                while (reader.MoveToNextEntry())
                                                {
                                                    if (imageExtensions.Contains(
                                                            Path.GetExtension(reader.Entry.Key)))
                                                    {
                                                        if (firstFileKey == null ||
                                                            string.Compare(reader.Entry.Key, firstFileKey,
                                                                StringComparison.OrdinalIgnoreCase) < 0)
                                                        {
                                                            firstFileKey = reader.Entry.Key;
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        if (firstFileKey != null)
                                        {
                                            key = firstFileKey;
                                            await using Stream stream = file.OpenRead();
                                            using var reader = ReaderFactory.Open(stream);
                                            while (reader.MoveToNextEntry())
                                            {
                                                if (reader.Entry.Key == firstFileKey)
                                                {
                                                    await using var entryStream = reader.OpenEntryStream();
                                                    await entryStream.CopyToAsync(
                                                        imageStream = new MemoryStream(), ct);
                                                    imageStream.Seek(0, SeekOrigin.Begin);
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    if (imageStream != null)
                                    {
                                        return new CoverDiscoveryResult(true, _buildCoverPath(file.FullName, key),
                                            Path.GetExtension(key)!, imageStream.ToArray());
                                    }
                                }
                                catch (Exception e)
                                {
                                    _logger.LogWarning(e,
                                        $"An error occurred during discovering covers from compressed files: {e.Message}.");
                                }
                                finally
                                {
                                    tryTimes++;
                                }

                            }
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        if (useIconAsFallback)
        {
            var iconData = ImageHelpers.ExtractIconAsPng(path);
            if (iconData != null)
            {
                const string ext = ".png";
                return new CoverDiscoveryResult(true, _buildCoverPath(path, $"icon{ext}"), "", iconData);
            }
        }

        return null;
    }

    private static string _buildCoverPath(string filePath, string? innerPath = null)
    {
        filePath = filePath.StandardizePath()!;
        const char separator = '>';
        return string.IsNullOrEmpty(innerPath) ? filePath : $"{filePath}{separator}{innerPath}";
    }
}