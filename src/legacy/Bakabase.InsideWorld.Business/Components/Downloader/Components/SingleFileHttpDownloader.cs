using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    public class SingleFileHttpDownloader : ISingleFileHttpDownloader
    {
        public SingleFileHttpDownloader(IHttpClientFactory httpClientFactory,
            ILogger<SingleFileHttpDownloader> logger) : this(
            httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default), logger)
        {

        }

        public SingleFileHttpDownloader(HttpClient httpClient, ILogger<SingleFileHttpDownloader> logger)
        {
            _client = httpClient;
            _logger = logger;
        }

        private readonly ILogger<SingleFileHttpDownloader> _logger;
        private readonly HttpClient _client;
        private const int DownloadBlockSize = 5_000_000;

        public event Func<int, Task>? OnProgress;

        public async Task Download(string url, string filePath, CancellationToken ct)
        {
            var rsp = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct);
            var remoteMd5Bytes = rsp.Content.Headers.ContentMD5;
            // if (remoteMd5Bytes == null)
            // {
            //     throw new Exception($"Got empty Content-MD5 from {url}");
            // }

            var fileSize = rsp.Content.Headers.ContentLength!.Value;
            var downloadUrl = rsp.RequestMessage!.RequestUri!.ToString();

            // assume the server supports HTTP Range
            var fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                if (fs.Length > fileSize)
                {
                    await fs.DisposeAsync();
                    File.Delete(filePath);
                    _logger.LogError(
                        $"Current file size: {fs.Length} is larger than expected: {fileSize} and will be deleted.");
                    fs = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }

                var p = fs.Length * 100 / fileSize;
                await TriggerOnProgress((int) p);

                if (fs.Length < fileSize)
                {
                    fs.Seek(0, SeekOrigin.End);
                    var downloadedBytesCount = fs.Length;
                    for (var blockStart = downloadedBytesCount;
                         blockStart < fileSize;
                         blockStart += DownloadBlockSize)
                    {
                        var downloadReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        downloadReq.Headers.Range = new RangeHeaderValue(blockStart,
                            Math.Min(fileSize, blockStart + DownloadBlockSize) - 1);
                        var blockRsp = await _client.SendAsync(downloadReq, ct);
                        blockRsp.EnsureSuccessStatusCode();
                        await blockRsp.Content.CopyToAsync(fs, ct);

                        p = fs.Length * 100 / fileSize;
                        await TriggerOnProgress((int) p);
                    }
                }

                if (fs.Length != fileSize)
                {
                    throw new Exception(
                        $"Current file size: {fs.Length} does not equal to expected: {fileSize}");
                }

                fs.Seek(0, SeekOrigin.Begin);
                var ms = new MemoryStream();
                await fs.CopyToAsync(ms, ct);
                ms.Seek(0, SeekOrigin.Begin);

                await fs.DisposeAsync();

                if (remoteMd5Bytes != null)
                {
                    var remoteMd5 = Convert.ToHexString(remoteMd5Bytes);
                    var localMd5Bytes = await MD5.Create().ComputeHashAsync(ms, ct);
                    var localMd5 = Convert.ToHexString(localMd5Bytes);
                    if (localMd5 != remoteMd5)
                    {
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

        protected virtual async Task TriggerOnProgress(int e)
        {
            if (OnProgress != null)
            {
                await OnProgress(e);
            }
        }
    }
}