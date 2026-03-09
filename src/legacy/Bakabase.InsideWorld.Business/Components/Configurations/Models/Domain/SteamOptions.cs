using System.Collections.Generic;
using System.Linq;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

public class SteamAccount
{
    public string? Name { get; set; }
    public string? ApiKey { get; set; }
    public string? SteamId { get; set; }
}

[Options(fileKey: "third-party-steam")]
public class SteamOptions
{
    public List<SteamAccount>? Accounts { get; set; }

    /// <summary>
    /// Convenience: gets the first account's API key.
    /// </summary>
    public string? ApiKey => Accounts?.FirstOrDefault()?.ApiKey;

    /// <summary>
    /// Convenience: gets the first account's Steam ID.
    /// </summary>
    public string? SteamId => Accounts?.FirstOrDefault()?.SteamId;

    public bool ShowCover { get; set; }
}
