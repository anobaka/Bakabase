using System.Security.Cryptography;
using System.Text;
using Bakabase.Modules.HttpDownloads.Models;
using TransferRequest = Bakabase.Modules.Downloader.Models.HttpDownloadRequest;

namespace Bakabase.Modules.HttpDownloads.Components;

public sealed partial class HttpDownloadManager
{
    private async Task<string> TransferAsync(StoredJob stored, CancellationToken ct)
    {
        var staging = StagingDirectory(stored);
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
        RestrictDirectory(Path.GetDirectoryName(staging)!);
        Directory.CreateDirectory(staging);
        RestrictDirectory(staging);
        if (stored.FileName != null && stored.ReadyContentHash != null)
        {
            var published = Path.Combine(stored.Job.OutputDirectory, stored.FileName);
            // Publishing intent is saved before the atomic rename. Recover only our verified bytes.
            if (stored.Publishing && File.Exists(published) && await MatchesReadyFileAsync(published, stored, ct))
                return published;
            var prepared = Path.Combine(staging, stored.FileName);
            if (File.Exists(prepared) && await MatchesReadyFileAsync(prepared, stored, ct))
                return await PublishAsync(stored, prepared, ct);
        }

        if (stored.RequestedFileName != null && File.Exists(Path.Combine(stored.Job.OutputDirectory, stored.RequestedFileName)))
            throw new DestinationExistsException();
        // This directory belongs exclusively to this job. The transport's independent checkpoint
        // retains partial bytes; files here are fully downloaded but not yet published.
        foreach (var path in Directory.EnumerateFiles(staging)) File.Delete(path);
        var file = await downloader.DownloadWithProgressAsync(new TransferRequest(stored.Url, staging)
        {
            FileName = stored.RequestedFileName, HttpClientName = stored.HttpClientName,
            ParallelConnections = stored.ParallelConnections, MaxRetries = stored.MaxRetries,
            MaximumBytesPerSecond = stored.MaximumBytesPerSecond, Timeout = stored.Timeout,
            AllowOverwrite = false
        }, async progress =>
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (stored.Job.Status != HttpDownloadStatus.Running) return;
                stored.Job = stored.Job with
                {
                    Stage = progress.Stage, Percentage = progress.Percentage is { } percentage ? Math.Clamp(percentage, 0, 99) : null,
                    CompletedBytes = Math.Max(0, progress.CompletedBytes), TotalBytes = progress.TotalBytes,
                    SpeedBytesPerSecond = progress.SpeedBytesPerSecond, UpdatedAt = DateTime.UtcNow
                };
            }
            finally { _gate.Release(); }
        }, ct);
        file = Path.GetFullPath(file);
        if (!SamePath(CanonicalDirectory(Path.GetDirectoryName(file)!), CanonicalDirectory(staging)) || !File.Exists(file))
            throw new InvalidDataException("The HTTP transport returned an invalid output file.");
        var fileName = Path.GetFileName(file);
        ValidateFileName(fileName);
        var length = new FileInfo(file).Length;
        var hash = await HashFileAsync(file, ct);
        await _gate.WaitAsync(ct);
        try
        {
            stored.FileName = fileName;
            stored.ReadyContentHash = hash;
            stored.ReadyLength = length;
            stored.Publishing = false;
            await CommitJobAsync(stored, stored.Job with
            {
                Stage = "Saving", CompletedBytes = length, TotalBytes = length,
                Percentage = 99, SpeedBytesPerSecond = null, UpdatedAt = DateTime.UtcNow
            });
        }
        finally { _gate.Release(); }
        return await PublishAsync(stored, file, ct);
    }

    private async Task<string> PublishAsync(StoredJob stored, string prepared, CancellationToken ct)
    {
        Directory.CreateDirectory(stored.Job.OutputDirectory);
        var target = Path.Combine(stored.Job.OutputDirectory, stored.FileName!);
        if (File.Exists(target) || Directory.Exists(target)) throw new DestinationExistsException();
        var temporary = Path.Combine(stored.Job.OutputDirectory, $".bakabase-http-{stored.Job.Id}.tmp");
        // Stage on the destination volume so publishing is an atomic rename, including NAS targets.
        try
        {
            await using (var source = File.OpenRead(prepared))
            await using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
                destination.Flush(true);
            }
            await _gate.WaitAsync(ct);
            try
            {
                stored.Publishing = true;
                await SaveAsync(stored);
                ct.ThrowIfCancellationRequested();
                try { File.Move(temporary, target, false); }
                catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { throw new DestinationExistsException(); }
            }
            finally { _gate.Release(); }
            File.Delete(prepared);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string StagingDirectory(StoredJob stored) => Path.Combine(_directory!, "staging", stored.Job.Id);

    private static async Task<bool> MatchesReadyFileAsync(string path, StoredJob stored, CancellationToken ct) =>
        new FileInfo(path).Length == stored.ReadyLength && await HashFileAsync(path, ct) == stored.ReadyContentHash;

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static HttpDownloadRequest Validate(HttpDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Provide an HTTP or HTTPS URL without embedded credentials.");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new ArgumentException("Choose an absolute output directory.");
        if (request.FileName != null) ValidateFileName(request.FileName);
        if (request.Timeout <= TimeSpan.Zero && request.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentException("The HTTP download timeout must be positive.");
        if (request.ParallelConnections is < 1 or > 16 || request.MaxRetries is < 0 or > 10 || request.MaximumBytesPerSecond < 0)
            throw new ArgumentException("Use 1–16 HTTP connections, 0–10 retries and a non-negative speed limit.");
        return request with
        {
            Url = new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri,
            OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.OutputDirectory)),
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey
        };
    }

    private static void ValidateStored(StoredJob stored)
    {
        if (stored.Job.Files == null || !Enum.IsDefined(stored.Job.Status))
            throw new InvalidDataException("The HTTP download record is invalid.");
        var request = Validate(new HttpDownloadRequest
        {
            Url = stored.Url, OutputDirectory = stored.Job.OutputDirectory, FileName = stored.RequestedFileName,
            HttpClientName = stored.HttpClientName, ParallelConnections = stored.ParallelConnections,
            MaxRetries = stored.MaxRetries, MaximumBytesPerSecond = stored.MaximumBytesPerSecond, Timeout = stored.Timeout
        });
        if (HashPayload(request) != stored.PayloadHash || stored.Job.Files.Any(path => !Path.IsPathFullyQualified(path) || !IsWithin(stored.Job.OutputDirectory, path)))
            throw new InvalidDataException("The HTTP download record is invalid.");
        if (stored.FileName != null) ValidateFileName(stored.FileName);
        if (stored.ReadyContentHash != null && (stored.FileName == null || stored.ReadyLength is null or < 0 ||
            stored.ReadyContentHash.Length != 64 || !stored.ReadyContentHash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The HTTP publication record is invalid.");
    }

    private static void ValidateFileName(string name)
    {
        var reserved = name.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || name is "." or ".." ||
            name.EndsWith('.') || name.EndsWith(' ') || name.Any(char.IsControl) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny("/\\:*?\"<>|".ToCharArray()) >= 0 ||
            reserved is "CON" or "PRN" or "AUX" or "NUL" ||
            reserved.Length == 4 && (reserved.StartsWith("COM") || reserved.StartsWith("LPT")) && reserved[3] is >= '1' and <= '9')
            throw new ArgumentException("FileName must be a single valid file name.");
    }

    private static string SuggestedFileName(string url)
    {
        var name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
        try { ValidateFileName(name); return name; }
        catch (ArgumentException) { return "HTTP download"; }
    }

    private static string HashPayload(HttpDownloadRequest request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(request.Url + "\n" + request.FileName + "\n" + request.HttpClientName)));

    private sealed class DestinationExistsException : IOException;

    // Network exceptions may embed credential-bearing URLs. Keep endpoint details out of task history.
    private static string SafeError(Exception exception) => exception switch
    {
        DestinationExistsException => "The output file already exists. Choose another name or directory.",
        TimeoutException => "The HTTP download timed out. Retry to continue from verified partial files.",
        OperationCanceledException => "The HTTP transfer was interrupted. Retry to continue from verified partial files.",
        UnauthorizedAccessException => "Access to the HTTP download directory was denied.",
        IOException => "The HTTP transfer failed while verifying data or accessing files. Retry or check the destination.",
        ArgumentException or InvalidDataException => "The HTTP download request or saved record is invalid.",
        _ => "The HTTP transfer failed. Check the source and network settings, then retry."
    };
}
