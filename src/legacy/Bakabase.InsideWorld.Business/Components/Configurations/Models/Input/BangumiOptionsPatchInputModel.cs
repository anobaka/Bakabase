using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class BangumiOptionsPatchInputModel
{
    public int? MaxConcurrency { get; set; }
    public int? RequestInterval { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}