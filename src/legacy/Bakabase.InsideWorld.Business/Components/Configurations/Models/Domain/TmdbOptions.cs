using System.Collections.Generic;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

[Options(fileKey: "third-party-tmdb")]
public record TmdbOptions : ITmdbOptions
{
    public int MaxConcurrency { get; set; } = 1;
    public int RequestInterval { get; set; } = 1000;
    public string? Cookie { get; set; }
    public string? UserAgent { set; get; }
    public string? Referer { set; get; }
    public Dictionary<string, string>? Headers { set; get; }
    public string? ApiKey { get; set; }
}