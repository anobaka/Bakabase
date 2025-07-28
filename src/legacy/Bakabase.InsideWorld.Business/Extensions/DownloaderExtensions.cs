using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Localization;
using NPOI.POIFS.Macros;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class DownloaderExtensions
    {
        public static async Task<ListResponse<DownloadTaskDbModel>> AddTasksAsync(this DownloadTaskAddInputModel model,
            IStringLocalizer localizer, CancellationToken cancellationToken = default)
        {
            // Validation is now handled in the downloader helpers via BuildTasks method
            // Basic null check
            if (model == null)
            {
                return ListResponseBuilder<DownloadTaskDbModel>.BuildBadRequest("Model cannot be null");
            }

            // Clean up keys - remove empty/null values
            model.Keys = model.Keys?.Where(k => !string.IsNullOrWhiteSpace(k)).ToList() ?? new List<string>();

            var doNotNeedKey = IsNoKeyRequiredTaskType(model.ThirdPartyId, model.Type);

            if (model.Keys?.Any() == true)
            {
                var tasks = new List<DownloadTaskDbModel>();
                
                for (int i = 0; i < model.Keys.Count; i++)
                {
                    var key = model.Keys[i];
                    var name = model.Names?.ElementAtOrDefault(i) ?? key; // Use key as name if no name provided
                    
                    tasks.Add(new DownloadTaskDbModel
                    {
                        ThirdPartyId = model.ThirdPartyId,
                        Interval = model.Interval,
                        Status = DownloadTaskStatus.InProgress,
                        Type = model.Type,
                        Key = key,
                        Name = name,
                        DownloadPath = model.DownloadPath,
                        AutoRetry = model.AutoRetry,
                        StartPage = model.StartPage,
                        EndPage = model.EndPage,
                        Checkpoint = model.Checkpoint,
                    });
                }
                
                return new ListResponse<DownloadTaskDbModel>(tasks);
            }

            if (doNotNeedKey)
            {
                var task = new DownloadTaskDbModel
                {
                    ThirdPartyId = model.ThirdPartyId,
                    Interval = model.Interval,
                    Status = DownloadTaskStatus.InProgress,
                    Type = model.Type,
                    DownloadPath = model.DownloadPath,
                    AutoRetry = model.AutoRetry,
                    StartPage = model.StartPage,
                    EndPage = model.EndPage,
                    Checkpoint = model.Checkpoint,
                };
                return new ListResponse<DownloadTaskDbModel>([task]);
            }

            return ListResponseBuilder<DownloadTaskDbModel>.BuildBadRequest(localizer[SharedResource.Downloader_KeyIsMissing]);
        }

        /// <summary>
        /// Synchronous version for backward compatibility (legacy method)
        /// </summary>
        public static ListResponse<DownloadTaskDbModel> AddTasks(this DownloadTaskAddInputModel model,
            IStringLocalizer localizer)
        {
            return model.AddTasksAsync(localizer).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Helper method to determine if a task type doesn't require keys
        /// </summary>
        private static bool IsNoKeyRequiredTaskType(ThirdPartyId thirdPartyId, int taskType)
        {
            return thirdPartyId switch
            {
                ThirdPartyId.ExHentai => (ExHentaiDownloadTaskType)taskType == ExHentaiDownloadTaskType.Watched,
                ThirdPartyId.Pixiv => (PixivDownloadTaskType)taskType == PixivDownloadTaskType.Following,
                _ => false
            };
        }
    }
}