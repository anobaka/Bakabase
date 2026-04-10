using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "third-party-soul-plus")]
public class SoulPlusOptions : ISoulPlusOptions
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
    public string? UserAgent
    {
        get => Accounts?.FirstOrDefault()?.UserAgent;
        set
        {
            if (Accounts is { Count: > 0 })
            {
                Accounts[0].UserAgent = value;
            }
        }
    }
    public string? TlsPreset
    {
        get => Accounts?.FirstOrDefault()?.TlsPreset;
        set
        {
            if (Accounts is { Count: > 0 })
            {
                Accounts[0].TlsPreset = value;
            }
        }
    }
    public string? Referer { set; get; }
    public Dictionary<string, string>? Headers { set; get; }
    public int AutoBuyThreshold { get; set; } = 10;
}