using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class DLsiteOptionsPatchInputModel
{
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public int? MaxConcurrency { get; set; }
    public int? RequestInterval { get; set; }
    public string? DefaultPath { get; set; }
    public string? NamingConvention { get; set; }
    public bool? SkipExisting { get; set; }
    public int? MaxRetries { get; set; }
    public int? RequestTimeout { get; set; }
}