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
    }
}