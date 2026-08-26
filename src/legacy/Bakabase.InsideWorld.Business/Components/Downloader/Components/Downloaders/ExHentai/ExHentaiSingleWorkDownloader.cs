using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiSingleWorkDownloader(
        IServiceProvider serviceProvider,
        IStringLocalizer<SharedResource> localizer,
        ExHentaiClient client,
        ITextVocabularyService specialTextService,
        IHostEnvironment env)
        : AbstractExHentaiDownloader(serviceProvider, localizer,
            client, specialTextService, env)
    {
        public override ExHentaiDownloadTaskType EnumTaskType => ExHentaiDownloadTaskType.SingleWork;

        protected override async Task StartCore(DownloadTask task, ExHentaiTaskOptions options, CancellationToken ct)
        {
            var exOptions = GetRequiredService<IBOptionsManager<ExHentaiOptions>>().Value;
            var manager = DownloaderManager;

            // A verdict recorded on the task survives restarts, so a re-run of a large set does not
            // walk every gallery again. While it holds, skip the torrent probe outright.
            var verdictIsFresh = ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(
                options.NoTorrentCheckedAt, exOptions.TorrentCheckValidityHours, DateTime.Now);
            var knownToHaveNoTorrent = verdictIsFresh || manager.IsKnownNoTorrent(task.Id);

            // Defer (probe-then-yield) only on the first pass of an un-probed task. Once it is known to
            // have no torrent, it is re-run with deferIfNoTorrent=false and downloads images normally.
            var deferIfNoTorrent = exOptions.PrioritizeTasksWithTorrent
                                   && options.PreferTorrent
                                   && !knownToHaveNoTorrent;

            await DownloadSingleWork(task.Key, task.Checkpoint, task.DownloadPath, OnNameAcquiredInternal,
                async current =>
                {
                    Current = current;
                    await OnCurrentChangedInternal();
                }, OnProgressInternal, OnCheckpointChangedInternal, ct,
                options.PreferTorrent && !knownToHaveNoTorrent,
                deferIfNoTorrent, () => manager.MarkNoTorrentAsync(task.Id));
        }
    }
}
