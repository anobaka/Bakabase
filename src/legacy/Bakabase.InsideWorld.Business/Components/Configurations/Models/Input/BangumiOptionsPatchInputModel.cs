using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class BangumiOptionsPatchInputModel
{
    public List<ThirdPartyAccount>? Accounts { get; set; }
    public int? MaxConcurrency { get; set; }
    public int? RequestInterval { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}