using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiWatchedDownloader(
        IServiceProvider serviceProvider,
        IStringLocalizer<SharedResource> localizer,
        ExHentaiClient client,
        ISpecialTextService specialTextService,
        IHostEnvironment env)
        : ExHentaiListDownloader(serviceProvider, localizer, client,
            specialTextService, env)
    {
        public override ExHentaiDownloadTaskType EnumTaskType => ExHentaiDownloadTaskType.Watched;

        protected override Task StartCore(DownloadTask task, ExHentaiTaskOptions options, CancellationToken ct)
        {
            if (task.Key.IsNullOrEmpty())
            {
                task.Key = "https://exhentai.org/watched";
            }
            return base.StartCore(task, options, ct);
        }
    }
}