using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public record NetworkOptionsPatchInputModel
{
    public record ProxyOptions
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string Address { get; set; } = null!;
        public NetworkOptions.ProxyOptions.ProxyCredentials? Credentials { get; set; }

        public NetworkOptions.ProxyOptions ToOptions()
        {
            return new NetworkOptions.ProxyOptions
            {
                Id = Id ?? Guid.NewGuid().ToString(),
                Name = Name,
                Address = Address,
                Credentials = Credentials
            };
        }

    }

    public List<ProxyOptions>? CustomProxies { get; set; }
    public NetworkOptions.ProxyModel? Proxy { get; set; }

    /// <summary>Null leaves the saved list untouched; an empty list clears it.</summary>
    public List<string>? CustomTestSites { get; set; }

    /// <summary>Null leaves the saved selection untouched; an empty list clears it.</summary>
    public List<string>? SelectedPresetTestSiteIds { get; set; }
}