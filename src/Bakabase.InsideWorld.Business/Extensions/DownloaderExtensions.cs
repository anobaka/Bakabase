﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public static ListResponse<DownloadTask> CreateTasks(this DownloadTaskCreateRequestModel model,
            IStringLocalizer localizer)
        {
            if (!SpecificEnumUtils<ThirdPartyId>.Values.Contains(model.ThirdPartyId))
            {
                return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                    localizer[SharedResource.Downloader_UnknownThirdPartyId, model.ThirdPartyId]);
            }

            // todo: extract abstractions
            var reservedValidTypes = new Dictionary<ThirdPartyId, int[]>
            {
                {
                    ThirdPartyId.Bilibili,
                    SpecificEnumUtils<BilibiliDownloadTaskType>.Values.Cast<int>().ToArray()
                },
                {
                    ThirdPartyId.ExHentai,
                    SpecificEnumUtils<ExHentaiDownloadTaskType>.Values.Cast<int>().ToArray()
                },
                {
                    ThirdPartyId.Pixiv,
                    SpecificEnumUtils<PixivDownloadTaskType>.Values.Cast<int>().ToArray()
                },
            };

            var validTypes = reservedValidTypes[model.ThirdPartyId];

            if (!validTypes.Contains(model.Type))
            {
                return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                    localizer[SharedResource.Downloader_UnknownType, model.Type]);
            }


            model.KeyAndNames =
                model.KeyAndNames?.Where(a => a.Key.IsNotEmpty()).ToDictionary(a => a.Key, a => a.Value);

            var doNotNeedKey = false;

            // todo: extract abstractions
            if (model.ThirdPartyId == ThirdPartyId.Bilibili)
            {
                switch ((BilibiliDownloadTaskType) model.Type)
                {
                    case BilibiliDownloadTaskType.Favorites:
                        if (model.KeyAndNames?.Any() != true ||
                            model.KeyAndNames.Any(a => !long.TryParse(a.Key, out _)))
                        {
                            return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                                localizer[SharedResource.Downloader_BilibiliFavoritesIsMissing]);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                if (model.ThirdPartyId == ThirdPartyId.ExHentai)
                {
                    switch ((ExHentaiDownloadTaskType) model.Type)
                    {
                        case ExHentaiDownloadTaskType.SingleWork:
                            if (model.KeyAndNames.IsNullOrEmpty())
                            {
                                return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                                    localizer[SharedResource.Downloader_KeyIsMissing]);
                            }

                            break;
                        case ExHentaiDownloadTaskType.Watched:
                            doNotNeedKey = true;
                            break;
                        case ExHentaiDownloadTaskType.List:
                            if (model.KeyAndNames.IsNullOrEmpty())
                            {
                                return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                                    localizer[SharedResource.Downloader_KeyIsMissing]);
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    if (model.ThirdPartyId == ThirdPartyId.Pixiv)
                    {
                        switch ((PixivDownloadTaskType) model.Type)
                        {
                            case PixivDownloadTaskType.Following:
                                doNotNeedKey = true;
                                break;
                            case PixivDownloadTaskType.Search:
                            case PixivDownloadTaskType.Ranking:
                                if (model.KeyAndNames.IsNullOrEmpty())
                                {
                                    return ListResponseBuilder<DownloadTask>.BuildBadRequest(
                                        localizer[SharedResource.Downloader_KeyIsMissing]);
                                }

                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (model.KeyAndNames?.Any() == true)
            {
                var tasks = model.KeyAndNames.Select(sn => new DownloadTask
                {
                    ThirdPartyId = model.ThirdPartyId,
                    Interval = model.Interval,
                    Status = DownloadTaskStatus.InProgress,
                    Type = model.Type,
                    Key = sn.Key,
                    Name = sn.Value,
                    DownloadPath = model.DownloadPath
                }).ToList();
                return new ListResponse<DownloadTask>(tasks);
            }

            if (doNotNeedKey)
            {
                var task = new DownloadTask
                {
                    ThirdPartyId = model.ThirdPartyId,
                    Interval = model.Interval,
                    Status = DownloadTaskStatus.InProgress,
                    Type = model.Type,
                    DownloadPath = model.DownloadPath
                };
                return new ListResponse<DownloadTask>(new[] {task});
            }

            return ListResponseBuilder<DownloadTask>.BuildBadRequest(localizer[SharedResource.Downloader_KeyIsMissing]);
        }
    }
}