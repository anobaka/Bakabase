using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Checkpoint;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Implementations
{
    public class ExHentaiTorrentDownloader : AbstractExHentaiDownloader
    {
        public ExHentaiTorrentDownloader(IServiceProvider serviceProvider, IStringLocalizer<SharedResource> localizer,
            ExHentaiClient client, ISpecialTextService specialTextService, IHostEnvironment env,
            IBOptionsManager<ExHentaiOptions> optionsManager) : base(serviceProvider, localizer, client,
            specialTextService, env, optionsManager)
        {
        }

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            var gallery = await Client.ParseDetail(task.Key, true);
            if (gallery.Torrents?.Any() != true)
            {
                throw new Exception("No torrent for this gallery");
            }

            var bestTorrent = gallery.Torrents!
                .OrderByDescending(t => t.Size)
                .ThenByDescending(t => t.UpdatedAt)
                .First();

            var path = Path.Combine(task.DownloadPath, gallery.Name.RemoveInvalidFilePathChars());
            await Client.DownloadTorrent(bestTorrent.DownloadUrl, path);
        }
    }
}