using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public abstract class AbstractPixivDownloader : AbstractDownloader
    {
        private readonly ISpecialTextService _specialTextService;
        protected readonly IBOptions<PixivOptions> Options;
        protected readonly PixivClient Client;
        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Pixiv;
        public abstract PixivDownloadTaskType TaskType { get; }

        public const string DefaultNamingConvention =
            $"{{{PixivNamingFields.UserName}}}/{{{PixivNamingFields.IllustrationId}}}-{{{PixivNamingFields.IllustrationTitle}}}-{{{PixivNamingFields.Tags}}}-p{{{PixivNamingFields.PageNo}}}{{{PixivNamingFields.Extension}}}";

        protected AbstractPixivDownloader(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IBOptions<PixivOptions> options, PixivClient client) : base(serviceProvider)
        {
            _specialTextService = specialTextService;
            Options = options;
            Client = client;
        }

        protected async Task ChangeCurrent(PixivNamingContext nc)
        {
            Current = $"[{nc.UserName}:{nc.UserId}]{nc.IllustrationId}:{nc.IllustrationTitle}";
            await OnCurrentChangedInternal();
        }

        protected string BuildKeyFilename(Dictionary<string, object> nameValues, Dictionary<string, string> wrappers) =>
            DownloaderUtils.BuildDownloadFilename<PixivNamingFields>(
                Options.Value.Downloader.NamingConvention.IsNotEmpty()
                    ? Options.Value.Downloader.NamingConvention
                    : DefaultNamingConvention, nameValues, wrappers).RemoveInvalidFilePathChars();

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
                var lastKeyFilename = BuildKeyFilename(lastFileNameValues, wrappers);
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
                var nameValues = new Dictionary<string, object>(baseNameValues)
                {
                    [PixivNamingFields.PageNo] = i,
                    [PixivNamingFields.Extension] = Path.GetExtension(pageUrl)
                };

                var keyFilename = BuildKeyFilename(nameValues, wrappers);
                var fullname = Path.Combine(downloadPath, keyFilename);
                if (!filePathAndUrls.ContainsKey(fullname) && !File.Exists(fullname))
                {
                    filePathAndUrls[fullname] = pageUrl;
                }
            }

            // Avoid large mount of tasks being created.
            var sm = new SemaphoreSlim(Options.Value.Downloader.Threads,
                Options.Value.Downloader.Threads);
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