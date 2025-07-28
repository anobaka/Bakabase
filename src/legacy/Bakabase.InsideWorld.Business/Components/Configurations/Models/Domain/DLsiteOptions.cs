using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "third-party-dlsite")]
public record DLsiteOptions : ISimpleDownloaderOptionsHolder, IThirdPartyHttpClientOptions
{
    public string? Cookie { get; set; }
    public string? UserAgent { get;set;  }
    public string? Referer { get;set;  }
    public Dictionary<string, string>? Headers { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public int RequestInterval { get; set; } = 1000;
    public string? DefaultPath { get; set; }
    public string? NamingConvention { get; set; }
    public bool SkipExisting { get; set; }
    public int MaxRetries { get; set; }
    public int RequestTimeout { get; set; }
}