using System.Collections.Generic;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "third-party-soul-plus")]
public record SoulPlusOptions : ISoulPlusOptions
{
    public int MaxConcurrency { get; set; } = 1;
    public int RequestInterval { get; set; } = 1000;
    public string? Cookie { get; set; }
    public string? UserAgent { set; get; }
    public string? Referer { set; get; }
    public Dictionary<string, string>? Headers { set; get; }
    public int AutoBuyThreshold { get; set; } = 10;
}