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
    /// Downloads one file over HTTP and verifies the length and any server-provided checksum.
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
        /// A complete existing file is reused only when it matches the current server checksum.
        /// Without a saved validator for existing partial bytes, retries safely start over.
        /// </para>
        /// </summary>
        public async Task<string> DownloadToDirectory(string url, string directory, CancellationToken ct)
        {
            Directory.CreateDirectory(directory);

            var probe = await Probe(url, ct);
            var fileName = ResolveFileName(probe, url);
            var filePath = Path.Combine(directory, fileName);

            await DownloadCore(url, filePath, probe, ct);

            return filePath;
        }

        public async Task Download(string url, string filePath, CancellationToken ct)
        {
            var probe = await Probe(url, ct);
            await DownloadCore(url, filePath, probe, ct);
        }

        private async Task DownloadCore(string url, string filePath, ProbeResult probe, CancellationToken ct)
        {
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
                if (fs.Length > 0)
                {
                    // Today's ETag cannot identify bytes left by a previous request. Do not append
                    // to or reuse those bytes based on size alone, even when If-Range is available.
                    var verifiedComplete = false;
                    if (fs.Length == expected && probe.Md5 is { } checksum)
                    {
                        using var md5 = MD5.Create();
                        verifiedComplete = (await md5.ComputeHashAsync(fs, ct)).SequenceEqual(checksum);
                    }
                    if (!verifiedComplete) fs.SetLength(0);
                }

                // Multiple responses need a stable version condition. A server without one is
                // downloaded in a single response so an in-flight change cannot mix byte ranges.
                var canUseRanges = probe.SupportsRange &&
                    (probe.ETag is {IsWeak: false} || probe.LastModified.HasValue);

                await TriggerOnProgress((int) (fs.Length * 100 / Math.Max(1, expected)));

                if (fs.Length < expected)
                {
                    if (!canUseRanges)
                    {
                        await fs.DisposeAsync();
                        await StreamWhole(downloadUrl, filePath, ct);
                        fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    }
                    else
                    {
                        fs.Seek(0, SeekOrigin.End);
                        while (fs.Length < expected)
                        {
                            var blockStart = fs.Length;
                            var blockEnd = Math.Min(expected, blockStart + DownloadBlockSize) - 1;
                            using var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

                            downloadReq.Headers.Range = new RangeHeaderValue(blockStart, blockEnd);
                            if (probe.ETag is {IsWeak: false})
                                downloadReq.Headers.IfRange = new RangeConditionHeaderValue(probe.ETag);
                            else if (probe.LastModified is { } modified)
                                downloadReq.Headers.IfRange = new RangeConditionHeaderValue(modified);
                            using var blockRsp = await httpClient.SendAsync(downloadReq,
                                HttpCompletionOption.ResponseHeadersRead, ct);

                            blockRsp.EnsureSuccessStatusCode();
                            if (blockRsp.StatusCode == HttpStatusCode.OK)
                            {
                                // A server may advertise ranges but ignore them, or If-Range may
                                // detect a changed file. Never append an entire response to a prefix.
                                await fs.DisposeAsync();
                                await StreamWhole(downloadUrl, filePath, ct);
                                fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                                break;
                            }
                            var range = blockRsp.Content.Headers.ContentRange;
                            if (blockRsp.StatusCode != HttpStatusCode.PartialContent || range?.From != blockStart ||
                                range.To != blockEnd || range.Length != expected)
                                throw new IOException("The server returned a different byte range than requested.");
                            await blockRsp.Content.CopyToAsync(fs, ct);
                            if (fs.Length != blockEnd + 1)
                                throw new IOException("The server returned an incomplete byte range.");

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

            if (total.HasValue && written != total.Value)
                throw new IOException($"The response ended after {written} bytes; {total.Value} were expected.");

            await TriggerOnProgress(100);
        }

        private async Task<ProbeResult> Probe(string url, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var rsp = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                rsp.EnsureSuccessStatusCode();
                return new ProbeResult(
                    rsp.Content.Headers.ContentLength,
                    rsp.Headers.AcceptRanges.Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase)),
                    rsp.Content.Headers.ContentMD5,
                    rsp.Content.Headers.ContentDisposition?.FileNameStar ?? rsp.Content.Headers.ContentDisposition?.FileName,
                    rsp.RequestMessage?.RequestUri?.ToString() ?? url,
                    rsp.Headers.ETag, rsp.Content.Headers.LastModified);
            }
            catch (HttpRequestException)
            {
                // Plenty of servers refuse HEAD. Ask for the first byte instead — it tells us the
                // same three things and costs nothing.
                using var req = new HttpRequestMessage(HttpMethod.Get, url);

                req.Headers.Range = new RangeHeaderValue(0, 0);
                using var rsp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                rsp.EnsureSuccessStatusCode();

                return new ProbeResult(
                    rsp.Content.Headers.ContentRange?.Length ?? rsp.Content.Headers.ContentLength,
                    rsp.StatusCode == HttpStatusCode.PartialContent,
                    // A range response's checksum describes that range, not the complete file.
                    rsp.StatusCode == HttpStatusCode.PartialContent ? null : rsp.Content.Headers.ContentMD5,
                    rsp.Content.Headers.ContentDisposition?.FileNameStar ??
                    rsp.Content.Headers.ContentDisposition?.FileName,
                    rsp.RequestMessage?.RequestUri?.ToString() ?? url,
                    rsp.Headers.ETag, rsp.Content.Headers.LastModified);
            }
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

            foreach (var c in Path.GetInvalidFileNameChars().Concat("/\\:*?\"<>|"))
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
            string FinalUrl,
            EntityTagHeaderValue? ETag,
            DateTimeOffset? LastModified);

        protected virtual async Task TriggerOnProgress(int e)
        {
            if (OnProgress != null)
            {
                await OnProgress(e);
            }
        }
    }
}
