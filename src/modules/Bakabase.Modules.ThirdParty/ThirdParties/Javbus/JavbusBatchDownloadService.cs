using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus;

/// <summary>
/// Runs one Javbus batch at a time and keeps its result table in memory for
/// the tool page to poll.
///
/// Nothing is persisted: covers already land on disk as they are fetched, and
/// the magnets are a list you copy out — a run that the app outlived is a run
/// you'd redo anyway.
/// </summary>
public class JavbusBatchDownloadService(JavbusClient client, ILoggerFactory loggerFactory)
{
    public const int MaxConcurrency = 8;

    private readonly ILogger _logger = loggerFactory.CreateLogger<JavbusBatchDownloadService>();
    private readonly Lock _lock = new();

    // Indexed by submission order and filled in place, so out-of-order workers
    // never reshuffle the table the user is reading.
    private JavbusBatchDownloadItem?[] _results = [];
    private bool _running;
    private string? _coverDirectory;
    private DateTime? _startedAt;
    private DateTime? _completedAt;

    public JavbusBatchDownloadState GetState()
    {
        lock (_lock)
        {
            var items = _results.Where(r => r != null).Select(r => r!).ToList();

            return new JavbusBatchDownloadState
            {
                IsRunning = _running,
                Total = _results.Length,
                Done = items.Count,
                CoverDirectory = _coverDirectory,
                StartedAt = _startedAt,
                CompletedAt = _completedAt,
                Items = items
            };
        }
    }

    public async Task Run(IReadOnlyList<string> codes, JavbusBatchDownloadSettings settings, BTaskArgs args)
    {
        var concurrency = Math.Clamp(settings.Concurrency, 1, MaxConcurrency);
        var delay = Math.Max(0, settings.DelayMs);
        var coverDirectory = string.IsNullOrWhiteSpace(settings.CoverDirectory) ? null : settings.CoverDirectory;
        if (coverDirectory != null)
        {
            Directory.CreateDirectory(coverDirectory);
        }

        lock (_lock)
        {
            _results = new JavbusBatchDownloadItem?[codes.Count];
            _running = true;
            _coverDirectory = coverDirectory;
            _startedAt = DateTime.Now;
            _completedAt = null;
        }

        try
        {
            await Parallel.ForEachAsync(codes.Select((code, index) => (code, index)),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = concurrency,
                    CancellationToken = args.CancellationToken
                },
                async (entry, ct) =>
                {
                    await args.YieldAsync();

                    var item = await Process(entry.code, coverDirectory, settings.SizeTolerance, ct);

                    int done;
                    lock (_lock)
                    {
                        _results[entry.index] = item;
                        done = _results.Count(r => r != null);
                    }

                    await args.UpdateTask(t =>
                    {
                        t.Percentage = done * 100 / codes.Count;
                        t.Process = $"{done}/{codes.Count}";
                    });

                    if (delay > 0)
                    {
                        await Task.Delay(delay, ct);
                    }
                });
        }
        finally
        {
            lock (_lock)
            {
                _running = false;
                _completedAt = DateTime.Now;
            }
        }
    }

    private async Task<JavbusBatchDownloadItem> Process(string code, string? coverDirectory, decimal sizeTolerance,
        CancellationToken ct)
    {
        try
        {
            var result = await client.SearchMagnets(code, ct);
            if (result == null)
            {
                return new JavbusBatchDownloadItem {Code = code, Status = JavbusBatchItemStatus.NotIndexed};
            }

            var magnet = JavbusMagnetSelector.Select(result.Magnets, sizeTolerance);
            if (magnet == null)
            {
                return new JavbusBatchDownloadItem
                {
                    Code = code,
                    Status = JavbusBatchItemStatus.NoMagnet,
                    Title = result.Title,
                    DetailUrl = result.DetailUrl,
                    CoverUrl = result.CoverUrl
                };
            }

            var (coverPath, coverError) = coverDirectory == null || string.IsNullOrEmpty(result.CoverUrl)
                ? (null, null)
                : await SaveCover(code, result.CoverUrl!, result.DetailUrl, coverDirectory, ct);

            return new JavbusBatchDownloadItem
            {
                Code = code,
                Status = JavbusBatchItemStatus.Succeeded,
                Title = result.Title,
                DetailUrl = result.DetailUrl,
                CoverUrl = result.CoverUrl,
                CoverPath = coverPath,
                CoverError = coverError,
                CandidateCount = result.Magnets.Count,
                Magnet = magnet
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Javbus batch failed on {Code}", code);

            return new JavbusBatchDownloadItem
            {
                Code = code,
                Status = JavbusBatchItemStatus.Failed,
                Error = e.Message
            };
        }
    }

    private async Task<(string? Path, string? Error)> SaveCover(string code, string coverUrl, string refererUrl,
        string directory, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(directory, $"{SanitizeFileName(code)}{ResolveExtension(coverUrl)}");
            await File.WriteAllBytesAsync(path, await client.DownloadCover(coverUrl, refererUrl, ct), ct);

            return (path, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }

    private static string SanitizeFileName(string code) =>
        string.Concat(code.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static string ResolveExtension(string coverUrl)
    {
        var extension = Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri)
            ? Path.GetExtension(uri.AbsolutePath)
            : Path.GetExtension(coverUrl);

        return string.IsNullOrEmpty(extension) ? ".jpg" : extension;
    }
}
