﻿using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models
{
    public class ExHentaiResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string RawName { get; set; }
        public ExHentaiCategory Category { get; set; }
        public string Url { get; set; }
        public string CoverUrl { get; set; }
        public int FileCount { get; set; }

        /// <summary>
        /// It's different for different row count
        /// </summary>
        public int PageCount { get; set; }

        public string TorrentPageUrl { get; set; }
        public List<ExHentaiTorrent>? Torrents { get; set; }
        public DateTime UpdateDt { get; set; }
        public decimal Rate { get; set; }
        public string Introduction { get; set; }
        public Dictionary<string, string[]> Tags { get; set; }
    }
}