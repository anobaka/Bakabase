using System;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Pixiv
{
    public static class PixivExtensions
    {
        public static PixivNamingContext ToNamingContext(this PixivSearchResponse.ListData.Work illustration)
        {
            return illustration == null
                ? null
                : new PixivNamingContext
                {
                    Extension = illustration.Url.IsNullOrEmpty() ? null : Path.GetExtension(illustration.Url),
                    IllustrationId = illustration.Id,
                    IllustrationTitle = illustration.Title,
                    PageCount = illustration.PageCount,
                    Tags = illustration.Tags?.ToArray(),
                    UploadDate = illustration.UpdateDate,
                    UserName = illustration.UserName,
                    UserId = illustration.UserId
                };
        }
        public static PixivNamingContext ToNamingContext(this FollowLatestIllustResponse.ThumbnailsModel.FollowIllust illustration)
        {
            return illustration == null
                ? null
                : new PixivNamingContext
                {
                    Extension = illustration.Url.IsNullOrEmpty() ? null : Path.GetExtension(illustration.Url),
                    IllustrationId = illustration.Id,
                    IllustrationTitle = illustration.Title,
                    PageCount = illustration.PageCount,
                    Tags = illustration.Tags?.ToArray(),
                    UploadDate = illustration.UploadDate,
                    UserName = illustration.UserName,
                    UserId = illustration.UserId
                };
        }

        public static PixivNamingContext ToNamingContext(this PixivRankingItem rankingItem)
        {
            return rankingItem == null
                ? null
                : new PixivNamingContext
                {
                    IllustrationId = rankingItem.Illust_id,
                    IllustrationTitle = rankingItem.Title,
                    Tags = rankingItem.Tags,
                    UploadDate = new DateTime(rankingItem.Illust_upload_timestamp * 1000),
                    UserId = rankingItem.User_id,
                    UserName = rankingItem.User_name,
                    PageCount = rankingItem.Illust_page_count,
                    Extension = Path.GetExtension(rankingItem.Url)
                };
        }
    }
}