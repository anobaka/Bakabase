using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Connectors;

/// <summary>
/// DLsite, as a place the user has bought things from.
/// <para>
/// The one platform here that hands files over directly: a purchase can be downloaded and
/// unpacked without anybody clicking anything, so a fetch runs to completion in one go.
/// </para>
/// </summary>
public class DLsiteConnector(IDLsiteWorkService works, ILogger<DLsiteConnector> logger)
    : IPlatformConnector
{
    public ResourceSource Source => ResourceSource.DLsite;

    public bool CanFetch => true;

    public async Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) =>
        (await works.GetAll())
        .Where(w => w.IsPurchased)
        .Select(w => new PlatformHolding(
            w.WorkId,
            w.Title ?? w.WorkId,
            w.LocalPath,
            w.CoverUrl == null ? null : [w.CoverUrl]))
        .ToList();

    public async Task<PlatformFetchOutcome> FetchAsync(string sourceKey, string workDirectory,
        Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        var work = await works.GetByWorkId(sourceKey);

        if (work == null)
        {
            return new PlatformFetchOutcome.Refused(
                $"{sourceKey} is not in your DLsite purchases. Sync them first, or buy it.");
        }

        if (!work.IsPurchased)
        {
            return new PlatformFetchOutcome.Refused($"{sourceKey} has not been bought.");
        }

        // Already downloaded and unpacked by an earlier run, or by the DLsite page itself. Steps
        // have to be idempotent, and this is what that means here.
        if (!string.IsNullOrEmpty(work.LocalPath) && System.IO.Directory.Exists(work.LocalPath))
        {
            return new PlatformFetchOutcome.Done(work.LocalPath);
        }

        async Task Progress(int percentage, string process)
        {
            if (onProgress != null) await onProgress(percentage, process);
        }

        // DLsite downloads into its own configured folder and extracts in place; the acquisition's
        // working directory is not where it goes, so the produced path is handed back instead.
        await works.DownloadWork(sourceKey, Progress, ct);
        await works.ExtractWork(sourceKey, Progress, ct);

        var after = await works.GetByWorkId(sourceKey);

        if (string.IsNullOrEmpty(after?.LocalPath))
        {
            logger.LogWarning("[DLsite] {WorkId} downloaded but has no local path", sourceKey);

            return new PlatformFetchOutcome.Refused(
                $"{sourceKey} was fetched but DLsite did not say where it went.");
        }

        return new PlatformFetchOutcome.Done(after.LocalPath);
    }

    public async Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct)
    {
        var work = await works.GetByWorkId(sourceKey);

        return string.IsNullOrEmpty(work?.LocalPath) ? null : work.LocalPath;
    }
}
