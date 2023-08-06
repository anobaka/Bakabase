using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Network
{
    public class InsideWorldWebProxy : IWebProxy
    {
        private readonly IBOptions<NetworkOptions> _options;

        public InsideWorldWebProxy(IBOptions<NetworkOptions> options)
        {
            _options = options;
        }

        public Uri? GetProxy(Uri destination) =>
            _options.Value.Proxy?.Address != null ? new Uri(_options.Value.Proxy.Address) : null;

        public bool IsBypassed(Uri host) => false;

        public ICredentials? Credentials
        {
            get
            {
                var c = _options.Value.Proxy?.Credentials;
                return c != null ? new NetworkCredential(c.Username, c.Password, c.Domain) : null;
            }
            set { }
        }
    }
}