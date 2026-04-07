using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    [Options(fileKey: "third-party-fanbox")]
    public class FanboxOptions : ISimpleDownloaderOptionsHolder
    {
        public List<ThirdPartyAccount>? Accounts { get; set; }

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
                    Accounts = [new ThirdPartyAccount { Cookie = value }];
                }
            }
        }

        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; } = 1000;
        public string? DefaultPath { get; set; }
        public string? NamingConvention { get; set; }
        public bool SkipExisting { get; set; }
        public int MaxRetries { get; set; }
        public int RequestTimeout { get; set; }
    }
}
