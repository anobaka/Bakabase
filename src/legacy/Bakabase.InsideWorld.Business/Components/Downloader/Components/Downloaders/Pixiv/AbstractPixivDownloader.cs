using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv
{
    public abstract class AbstractPixivDownloader : AbstractDownloader<PixivDownloadTaskType>
    {
        private readonly ISpecialTextService _specialTextService;
        protected readonly PixivClient Client;
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Pixiv;
        protected AbstractPixivDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            PixivClient client) : base(serviceProvider)
        {
            _specialTextService = specialTextService;
            Client = client;
        }

        protected async Task ChangeCurrent(PixivNamingContext nc)
        {
            Current = $"[{nc.UserName}:{nc.UserId}]{nc.IllustrationId}:{nc.IllustrationTitle}";
            await OnCurrentChangedInternal();
        }

        protected async Task<string> BuildKeyFilename(Dictionary<PixivNamingFields, object> nameValues)
        {
            return await BuildDownloadFilename(nameValues);
        }

        protected async Task DownloadSingleWork(string id, string downloadPath, PixivNamingContext nameContext, CancellationToken ct)
        {
            await ChangeCurrent(nameContext);

            var wrappers =
                (await _specialTextService.GetAll(a => a.Type == SpecialTextType.Wrapper))
                .Where(a => a.Value1.IsNotEmpty() && a.Value2.IsNotEmpty())
                .GroupBy(a => a.Value1)
                .ToDictionary(a => a.Key, a => a.FirstOrDefault()!.Value2);

            var lastFileNameValues = nameContext.ToLastFileNameValues();
            if (lastFileNameValues != null)
            {
                var lastKeyFilename = await BuildKeyFilename(lastFileNameValues);
                {
                    var fullname = Path.Combine(downloadPath, lastKeyFilename);
                    if (File.Exists(fullname))
                    {
                        return;
                    }
                }
            }

            var illustration = await Client.GetIllustrationInfo(id);
            var firstPageBestQualityUrl = illustration.Urls.Original;
            var pageCount = illustration.PageCount;
            var filePathAndUrls = new Dictionary<string, string>();
            var baseNameValues = nameContext.ToBaseNameValues();
            for (var i = 0; i < pageCount; i++)
            {
                var pageUrl = firstPageBestQualityUrl.Replace("_p0", $"_p{i}");
                var nameValues = new Dictionary<PixivNamingFields, object>(baseNameValues)
                {
                    [PixivNamingFields.PageNo] = i,
                    [PixivNamingFields.Extension] = Path.GetExtension(pageUrl)
                };

                var keyFilename = await BuildKeyFilename(nameValues);
                var fullname = Path.Combine(downloadPath, keyFilename);
                if (!filePathAndUrls.ContainsKey(fullname) && !File.Exists(fullname))
                {
                    filePathAndUrls[fullname] = pageUrl;
                }
            }

            // Avoid large mount of tasks being created.
            var options = await GetDownloaderOptionsAsync();
            var threads = options.Cookie != null && options.Cookie.Contains("thread") ? 5 : 10; // Default thread count
            var sm = new SemaphoreSlim(threads, threads);
            var tasks = new ConcurrentBag<Task>();

            foreach (var (fullname, pageUrl) in filePathAndUrls)
            {
                var dir = Path.GetDirectoryName(fullname)!;
                Directory.CreateDirectory(dir);
                await sm.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        const int maxTryTimes = 10;
                        var tryTimes = 0;
                        byte[] data;
                        while (true)
                        {
                            try
                            {
                                data = await Client.GetBytes(pageUrl);
                                break;
                            }
                            catch (Exception)
                            {
                                tryTimes++;
                                if (tryTimes >= maxTryTimes)
                                {
                                    throw;
                                }
                            }
                        }

                        await File.WriteAllBytesAsync(fullname, data, ct);
                    }
                    finally
                    {
                        sm.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
        }
    }
}