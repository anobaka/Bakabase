using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
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

        public Dictionary<string, object> ToBaseNameValues()
        {
            return new Dictionary<string, object>
            {
                {PixivNamingFields.IllustrationId, IllustrationId},
                {PixivNamingFields.IllustrationTitle, IllustrationTitle},
                {PixivNamingFields.UploadDate, UploadDate?.ToString("yyyy-MM-dd")},
                {PixivNamingFields.Tags, Tags?.Length > 0 ? string.Join(',', Tags) : null},
                {PixivNamingFields.UserId, UserId},
                {PixivNamingFields.UserName, UserName}
            };
        }

        public Dictionary<string, object> ToLastFileNameValues()
        {
            if (PageCount > 0 && Extension.IsNotEmpty())
            {
                var nv = ToBaseNameValues();
                var predictedLastFileNameValues = new Dictionary<string, object>(nv)
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