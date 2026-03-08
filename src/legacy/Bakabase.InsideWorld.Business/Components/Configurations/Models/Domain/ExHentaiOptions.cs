using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    public class ExHentaiAccount
    {
        public string? Name { get; set; }
        public string? Cookie { get; set; }
    }

    [Options(fileKey: "third-party-exhentai")]
    public class ExHentaiOptions: ISimpleDownloaderOptionsHolder, IThirdPartyHttpClientOptions
    {
        public List<ExHentaiAccount>? Accounts { get; set; }

        /// <summary>
        /// Backward compatible: reads/writes first account's cookie.
        /// </summary>
        public string? Cookie
        {
            get => Accounts?.FirstOrDefault()?.Cookie;
            set
            {
                if (Accounts is { Count: > 0 })
                {
                    Accounts[0].Cookie = value;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    Accounts = [new ExHentaiAccount { Cookie = value }];
                }
            }
        }

        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; } = 1000;
        public string? DefaultPath { get; set; }
        public string? NamingConvention { get; set; }
        public bool SkipExisting { get; set; }
        public int MaxRetries { get; set; }
        public int RequestTimeout { get; set; }
    }
}
