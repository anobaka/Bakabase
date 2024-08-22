using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.ThirdParty.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class ExHentaiWatchedDownloader : ExHentaiListDownloader
    {
        public ExHentaiWatchedDownloader(IServiceProvider serviceProvider, IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ISpecialTextService specialTextService, IHostEnvironment env,
            IBOptionsManager<ExHentaiOptions> optionsManager) : base(serviceProvider, localizer, client,
            specialTextService, env, optionsManager)
        {

        }

        protected override Task StartCore(DownloadTask task, CancellationToken ct)
        {
            if (task.Key.IsNullOrEmpty())
            {
                task.Key = "https://exhentai.org/watched";
            }
            return base.StartCore(task, ct);
        }
    }
}