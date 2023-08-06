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
        public class ProxyOptions
        { 
            public string Address { get; set; } = string.Empty;
            public ProxyCredentials? Credentials { get; set; }

            public class ProxyCredentials
            {
                public string Username { get; set; } = string.Empty;
                public string? Password { get; set; }
                public string? Domain { get; set; }
            }
        }
        public ProxyOptions? Proxy { get; set; }
    }
}