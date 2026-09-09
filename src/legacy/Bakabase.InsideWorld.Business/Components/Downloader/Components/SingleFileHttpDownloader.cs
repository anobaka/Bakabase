using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Downloads one file over HTTP, resuming where it can.
    /// </summary>
    public class SingleFileHttpDownloader(HttpClient httpClient, ILogger<SingleFileHttpDownloader> logger)
    {
        private const int DownloadBlockSize = 5_000_000;

        /// <summary>Streamed rather than loaded, so a 20 GB file costs 80 KB of memory.</summary>
        private const int HashBufferSize = 81920;

        public event Func<int, Task>? OnProgress;

        /// <summary>
        /// Downloads into <paramref name="directory"/>, naming the file the way the server does, and
        /// returns where it ended up.
        /// <para>
        /// Idempotent on purpose: a file already there at the full length is left alone, so a step
        /// re-run after a restart does not download it again.
        /// </para>
        /// </summary>
        public async Task<string> DownloadToDirectory(string url, string directory, CancellationToken ct)
        {
            Directory.CreateDirectory(directory);

            var probe = await Probe(url, ct);
            var fileName = ResolveFileName(probe, url);
            var filePath = Path.Combine(directory, fileName);

            await Download(url, filePath, ct);

            return filePath;
        }

        public async Task Download(string url, string filePath, CancellationToken ct)
        {
            var probe = await Probe(url, ct);
            var fileSize = probe.Length;
            var downloadUrl = probe.FinalUrl;

            if (fileSize is not { } expected)
            {
                // No Content-Length: chunked, or a server that will not say. Resuming needs a
                // length to resume to, so this streams the whole thing in one go instead of
                // failing on a null the way it used to.
                logger.LogInformation(
                    "[Download] {Url} did not say how large it is; downloading in one pass", url);
                await StreamWhole(downloadUrl, filePath, ct);

                return;
            }

            var fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                if (fs.Length > expected)
                {
                    await fs.DisposeAsync();
                    File.Delete(filePath);
                    logger.LogError(
                        $"Current file size: {fs.Length} is larger than expected: {expected} and will be deleted.");
                    fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }

                await TriggerOnProgress((int) (fs.Length * 100 / Math.Max(1, expected)));

                if (fs.Length < expected)
                {
                    if (!probe.SupportsRange && fs.Length > 0)
                    {
                        // A partial file is worthless against a server that cannot resume; start over
                        // rather than appending the whole body onto what is already there.
                        fs.SetLength(0);
                    }

                    if (!probe.SupportsRange)
                    {
                        await fs.DisposeAsync();
                        await StreamWhole(downloadUrl, filePath, ct);
                        fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    }
                    else
                    {
                        fs.Seek(0, SeekOrigin.End);
                        for (var blockStart = fs.Length; blockStart < expected; blockStart += DownloadBlockSize)
                        {
                            var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

                            downloadReq.Headers.Range = new RangeHeaderValue(blockStart,
                                Math.Min(expected, blockStart + DownloadBlockSize) - 1);
                            var blockRsp = await httpClient.SendAsync(downloadReq, ct);

                            blockRsp.EnsureSuccessStatusCode();
                            await blockRsp.Content.CopyToAsync(fs, ct);

                            await TriggerOnProgress((int) (fs.Length * 100 / expected));
                        }
                    }
                }

                if (fs.Length != expected)
                {
                    throw new Exception($"Current file size: {fs.Length} does not equal to expected: {expected}");
                }

                if (probe.Md5 is { } remoteMd5Bytes)
                {
                    // Streamed through the hash rather than copied into memory first: the old
                    // version made a second full copy of every file it verified.
                    fs.Seek(0, SeekOrigin.Begin);
                    using var md5 = MD5.Create();
                    var localMd5 = Convert.ToHexString(await md5.ComputeHashAsync(fs, ct));
                    var remoteMd5 = Convert.ToHexString(remoteMd5Bytes);

                    if (localMd5 != remoteMd5)
                    {
                        await fs.DisposeAsync();
                        File.Delete(filePath);

                        throw new Exception(
                            $"Failed to check MD5 for downloaded file, got: {localMd5} but expected: {remoteMd5}");
                    }
                }
            }
            finally
            {
                try
                {
                    await fs.DisposeAsync();
                }
                catch
                {
                    // ignored
                }
            }
        }

        private async Task StreamWhole(string url, string filePath, CancellationToken ct)
        {
            using var rsp = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            rsp.EnsureSuccessStatusCode();

            var total = rsp.Content.Headers.ContentLength;

            await using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var source = await rsp.Content.ReadAsStreamAsync(ct);

            var buffer = new byte[HashBufferSize];
            long written = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;

                if (total is > 0)
                {
                    await TriggerOnProgress((int) (written * 100 / total.Value));
                }
            }

            await TriggerOnProgress(100);
        }

        private async Task<ProbeResult> Probe(string url, CancellationToken ct)
        {
            HttpResponseMessage rsp;
            try
            {
                rsp = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct);
                rsp.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                // Plenty of servers refuse HEAD. Ask for the first byte instead — it tells us the
                // same three things and costs nothing.
                var req = new HttpRequestMessage(HttpMethod.Get, url);

                req.Headers.Range = new RangeHeaderValue(0, 0);
                rsp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                rsp.EnsureSuccessStatusCode();

                return new ProbeResult(
                    rsp.Content.Headers.ContentRange?.Length,
                    rsp.StatusCode == HttpStatusCode.PartialContent,
                    rsp.Content.Headers.ContentMD5,
                    rsp.Content.Headers.ContentDisposition?.FileNameStar ??
                    rsp.Content.Headers.ContentDisposition?.FileName,
                    rsp.RequestMessage?.RequestUri?.ToString() ?? url);
            }

            return new ProbeResult(
                rsp.Content.Headers.ContentLength,
                rsp.Headers.AcceptRanges.Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase)),
                rsp.Content.Headers.ContentMD5,
                rsp.Content.Headers.ContentDisposition?.FileNameStar ??
                rsp.Content.Headers.ContentDisposition?.FileName,
                rsp.RequestMessage?.RequestUri?.ToString() ?? url);
        }

        /// <summary>
        /// What the file should be called: what the server said, else the last path segment, else a
        /// generic name. Quotes and directory separators are stripped — the name comes from a remote
        /// server, and it is about to be joined onto a local path.
        /// </summary>
        private static string ResolveFileName(ProbeResult probe, string url)
        {
            var candidate = probe.FileName?.Trim('"', '\'', ' ');

            if (string.IsNullOrWhiteSpace(candidate) &&
                Uri.TryCreate(probe.FinalUrl, UriKind.Absolute, out var uri))
            {
                candidate = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = "download";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(c, '_');
            }

            // "..", "." and a name that reduced to nothing all resolve outside the directory.
            candidate = candidate.Trim('.', ' ');

            return string.IsNullOrWhiteSpace(candidate) ? "download" : candidate;
        }

        private record ProbeResult(
            long? Length,
            bool SupportsRange,
            byte[]? Md5,
            string? FileName,
            string FinalUrl);

        protected virtual async Task TriggerOnProgress(int e)
        {
            if (OnProgress != null)
            {
                await OnProgress(e);
            }
        }
    }
}
