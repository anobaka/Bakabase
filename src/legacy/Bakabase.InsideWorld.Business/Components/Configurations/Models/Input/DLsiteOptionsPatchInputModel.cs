using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class DLsiteOptionsPatchInputModel
{
    public List<DLsiteAccount>? Accounts { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public int? MaxConcurrency { get; set; }
    public int? RequestInterval { get; set; }
    public string? DefaultPath { get; set; }
    public List<string>? ScanFolders { get; set; }
    public string? NamingConvention { get; set; }
    public bool? SkipExisting { get; set; }
    public int? MaxRetries { get; set; }
    public int? RequestTimeout { get; set; }
    public bool? ShowCover { get; set; }
    public bool? DeleteArchiveAfterExtraction { get; set; }
}