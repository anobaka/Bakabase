using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
{
    public class PixivNamingFields
    {
        [DownloaderNamingField(Example = "105862402", Description = "Id of illustration")]
        public const string IllustrationId = nameof(IllustrationId);

        [DownloaderNamingField(Example = "鈴谷とJK", Description = "Title of illustration")]
        public const string IllustrationTitle = nameof(IllustrationTitle);

        [DownloaderNamingField(Example = "2023-01-02", Description = "Upload date of illustration")]
        public const string UploadDate = nameof(UploadDate);

        [DownloaderNamingField(Example = "艦これ,鈴谷,熊野,オリジナル提", Description = "Tags of illustration")]
        public const string Tags = nameof(Tags);

        [DownloaderNamingField(Example = "4832919", Description = "Id of user(uploader)")]
        public const string UserId = nameof(UserId);

        [DownloaderNamingField(Example = "ムンム’s", Description = "Name of user(uploader)")]
        public const string UserName = nameof(UserName);

        [DownloaderNamingField(Example = "0", Description = "Number of page, starts from 0")]
        public const string PageNo = nameof(PageNo);

        [DownloaderNamingField(Example = ".png", Description = "Extension of file with trailing dot")]
        public const string Extension = nameof(Extension);
    }
}