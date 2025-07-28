using System;
using System.Collections.Generic;
using System.IO;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Pixiv
{
    public class PixivNamingContext
    {
        public string IllustrationId { get; set; }
        public string IllustrationTitle { get; set; }
        public DateTime? UploadDate { get; set; }
        public string[] Tags { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public int PageCount { get; set; }
        public string Extension { get; set; }

        public Dictionary<PixivNamingFields, object> ToBaseNameValues()
        {
            return new Dictionary<PixivNamingFields, object>
            {
                {PixivNamingFields.IllustrationId, IllustrationId},
                {PixivNamingFields.IllustrationTitle, IllustrationTitle},
                {PixivNamingFields.UploadDate, UploadDate?.ToString("yyyy-MM-dd")},
                {PixivNamingFields.Tags, Tags?.Length > 0 ? string.Join(',', Tags) : null},
                {PixivNamingFields.UserId, UserId},
                {PixivNamingFields.UserName, UserName}
            };
        }

        public Dictionary<PixivNamingFields, object> ToLastFileNameValues()
        {
            if (PageCount > 0 && Extension.IsNotEmpty())
            {
                var nv = ToBaseNameValues();
                var predictedLastFileNameValues = new Dictionary<PixivNamingFields, object>(nv)
                {
                    [PixivNamingFields.PageNo] = PageCount - 1,
                    [PixivNamingFields.Extension] = Path.GetExtension(Extension)
                };
                return predictedLastFileNameValues;
            }

            return null;
        }
    }
}