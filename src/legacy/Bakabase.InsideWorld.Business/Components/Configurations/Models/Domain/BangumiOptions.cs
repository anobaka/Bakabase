using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain
{
    [Options(fileKey: "third-party-bangumi")]
    public class BangumiOptions : IThirdPartyHttpClientOptions
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
        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }
}
