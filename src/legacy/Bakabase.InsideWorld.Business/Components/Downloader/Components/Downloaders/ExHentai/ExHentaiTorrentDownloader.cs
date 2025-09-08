using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiTorrentDownloader(
        IServiceProvider serviceProvider,
        IStringLocalizer<SharedResource> localizer,
        ExHentaiClient client,
        ISpecialTextService specialTextService,
        IHostEnvironment env)
        : AbstractExHentaiDownloader(serviceProvider, localizer, client,
            specialTextService, env)
    {
        public override ExHentaiDownloadTaskType EnumTaskType => ExHentaiDownloadTaskType.Torrent;

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            var gallery = await Client.ParseDetail(task.Key, true);
            task.Name = gallery.RawName ?? gallery.Name;
            if (gallery.Torrents?.Any() != true)
            {
                throw new Exception("No torrent for this gallery");
            }
            
            var betterName = gallery.RawName.IsNullOrEmpty() ? gallery.Name : gallery.RawName;
            await OnNameAcquiredInternal(betterName);

            var bestTorrent = gallery.Torrents!
                .OrderByDescending(t => t.Size)
                .ThenByDescending(t => t.UpdatedAt)
                .First();

            var path = Path.Combine(task.DownloadPath, $"{gallery.Name.RemoveInvalidFilePathChars()}.torrent");
            await Client.DownloadTorrent(bestTorrent.DownloadUrl, path);
        }
    }
}