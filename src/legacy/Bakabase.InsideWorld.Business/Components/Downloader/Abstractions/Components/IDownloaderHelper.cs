using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Localization;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;

public interface IDownloaderHelper
{
    Task<DownloaderOptions> GetOptionsAsync();
    Task PutOptionsAsync(DownloaderOptions options);
    Task ValidateOptionsAsync();

    Task<DownloadTask[]> BuildTasks(DownloadTaskAddInputModel model);
}