using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
{
    public class BilibiliNamingFields
    {
        [DownloaderNamingField(Example = "8047632",
            Description =
                "Id of a uploader. It can be found in their space url usually. The '8047632' in https://space.bilibili.com/8047632 for example.")]
        public const string UploaderId = nameof(UploaderId);

        [DownloaderNamingField(Example = "哔哩哔哩弹幕网", Description = "This name could be changed by uploader sometimes.")]
        public const string UploaderName = nameof(UploaderName);

        [DownloaderNamingField(Example = "563808828", Description = "The number id of a post.")]
        public const string AId = nameof(AId);

        [DownloaderNamingField(Example = "BV1hv4y197fB", Description = "The string id of a post.")]
        public const string BvId = nameof(BvId);

        [DownloaderNamingField(Example = "B站2022年度弹幕，两个字",
            Description = "The title of a post, this can be changed by uploader.")]
        public const string PostTitle = nameof(PostTitle);

        [DownloaderNamingField(Example = "924142224", Description = "Part id of a post.")]
        public const string CId = nameof(CId);

        [DownloaderNamingField(Example = "1", Description = "Part no of a post, it starts from 1.")]
        public const string PartNo = nameof(PartNo);

        [DownloaderNamingField(Example = "B站2022年度弹幕，两个字",
            Description =
                "Part name of a post, it could be changed by uploader. And if there is only one part, the name will be ignored if post name is applied.")]
        public const string PartName = nameof(PartName);

        [DownloaderNamingField(Example = "1080P 高码率", Description = "Quality name of video.")]
        public const string QualityName = nameof(QualityName);

        [DownloaderNamingField(Example = ".mp4", Description = "Extension of video with leading dot.")]
        public const string Extension = nameof(Extension);
    }
}