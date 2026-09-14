using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>Runs and observes one aria2 job, stopping only that job when interrupted.</summary>
public sealed class Aria2Downloader(IHttpClientFactory httpClientFactory, ILogger<Aria2Downloader> logger)
    : IAria2Downloader
{
    public async Task<TorrentDownloadResult> DownloadMagnetAsync(string magnet, string workingDirectory,
        Aria2DownloadOptions options, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (!TorrentMetadata.IsValidMagnet(magnet))
            throw new ArgumentException("Enter a magnet link with a valid BitTorrent info hash.", nameof(magnet));
        if (!Uri.TryCreate(options.RpcUrl, UriKind.Absolute, out var rpcUri) || rpcUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("aria2 needs an HTTP(S) RPC URL.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(options), "The download timeout must be between zero and 30 days.");
        if (options.PollInterval <= TimeSpan.Zero || options.PollInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(options), "The poll interval must be between zero and 60 seconds.");

        ct.ThrowIfCancellationRequested();
        using var http = httpClientFactory.CreateClient(DownloaderServiceCollectionExtensions.HttpClientName);
        var payloadDirectory = Path.Combine(Path.GetFullPath(workingDirectory), "torrent-data");
        Directory.CreateDirectory(payloadDirectory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.Timeout);
        string? gid = null;
        var completed = false;
        try
        {
            try
            {
                gid = Aria2Rpc.ReadGid(await Call(http, options.RpcUrl,
                    Aria2Rpc.BuildAddUri(magnet, payloadDirectory, options.Secret, "add"), deadline.Token));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new IOException($"Could not reach aria2 at {options.RpcUrl}: {ex.Message}", ex);
            }
            logger.LogInformation("aria2 took the magnet as {Gid}", gid);

            while (true)
            {
                await Task.Delay(options.PollInterval, deadline.Token);
                Aria2Status status;
                try
                {
                    status = Aria2Rpc.ReadStatus(await Call(http, options.RpcUrl,
                        Aria2Rpc.BuildTellStatus(gid, options.Secret, "status"), deadline.Token));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw new IOException($"aria2 stopped answering: {ex.Message}", ex); }

                if (status.IsFailed)
                    throw new IOException($"aria2 gave up on the magnet: {status.ErrorMessage ?? status.Status}");
                // Metadata completion names the separate job that downloads the real files.
                if (status.IsComplete && status.FollowedBy is { } next)
                {
                    // Update ownership before invoking caller code, which can throw or cancel.
                    gid = next;
                    logger.LogInformation("The metadata resolved; the files are coming as {Gid}", next);
                    if (progress != null) await progress(status.Percentage, "Fetching the torrent");
                    continue;
                }
                if (progress != null) await progress(status.Percentage, "Fetching the torrent");
                if (!status.IsComplete) continue;

                var files = status.Files.Select(Path.GetFullPath).Distinct().ToList();
                if (files.Any(file => !file.StartsWith(payloadDirectory + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new IOException("aria2 returned a file outside this download's directory.");
                if (files.Count == 0 || files.Any(file => !File.Exists(file)))
                    throw new IOException("aria2 reported the magnet complete but its files are missing from the download directory.");

                completed = true;
                return new TorrentDownloadResult(payloadDirectory, files);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested &&
            (deadline.IsCancellationRequested || ex.InnerException is TimeoutException))
        {
            throw new TimeoutException(deadline.IsCancellationRequested
                ? $"The magnet was still not finished after {options.Timeout.TotalMinutes:0.##} minutes."
                : "The aria2 request timed out.", ex);
        }
        finally
        {
            if (!completed && gid != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await Call(http, options.RpcUrl, Aria2Rpc.BuildForceRemove(gid, options.Secret, "stop"), cleanup.Token); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not stop aria2 download {Gid}", gid); }
            }
        }
    }

    private static async Task<string> Call(HttpClient http, string rpcUrl, string body, CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, rpcUrl) {Content = content};
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
