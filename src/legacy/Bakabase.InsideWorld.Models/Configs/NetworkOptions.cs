using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options()]
    public class NetworkOptions
    {
        public enum ProxyMode
        {
            DoNotUse = 0,
            UseSystem = 1,
            UseCustom = 2
        }

        public record ProxyModel
        {
            public ProxyMode Mode { get; set; }
            public string? CustomProxyId { get; set; }
        }

        public record ProxyOptions
        {
            public string Id { get; set; } = null!;

            /// <summary>
            /// User-facing label. Optional — falls back to <see cref="Address"/> for display,
            /// which is all the UI had to identify a proxy by before.
            /// </summary>
            public string? Name { get; set; }

            public string Address { get; set; } = null!;
            public ProxyCredentials? Credentials { get; set; }

            public class ProxyCredentials
            {
                public string Username { get; set; } = null!;
                public string? Password { get; set; }
                public string? Domain { get; set; }
            }
        }

        public List<ProxyOptions>? CustomProxies { get; set; }
        public ProxyModel Proxy { get; set; } = new() {Mode = ProxyMode.DoNotUse};

        /// <summary>
        /// Extra URLs the user wants a proxy connectivity test to hit, on top of the
        /// built-in presets. Null/empty means presets only.
        /// </summary>
        public List<string>? CustomTestSites { get; set; }

        /// <summary>
        /// Ids of the built-in preset sites the user has selected for testing. Null means
        /// "not configured yet" and the caller applies its own default selection.
        /// </summary>
        public List<string>? SelectedPresetTestSiteIds { get; set; }
    }
}