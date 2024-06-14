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
    public class InsideWorldWebProxy(IBOptions<NetworkOptions> options) : IWebProxy
    {
        public Uri? GetProxy(Uri destination)
        {
            switch (options.Value.Proxy.Mode)
            {
                case NetworkOptions.ProxyMode.DoNotUse:
                    return null;
                case NetworkOptions.ProxyMode.UseSystem:
                    return WebRequest.GetSystemWebProxy().GetProxy(destination);
                case NetworkOptions.ProxyMode.UseCustom:
                    var p = (options.Value.CustomProxies?.FirstOrDefault(x =>
                        x.Id == options.Value.Proxy.CustomProxyId));
                    return !string.IsNullOrEmpty(p?.Address) ? new Uri(p.Address) : null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool IsBypassed(Uri host) => false;

        public ICredentials? Credentials
        {
            get
            {
                switch (options.Value.Proxy.Mode)
                {
                    case NetworkOptions.ProxyMode.DoNotUse:
                        return null;
                    case NetworkOptions.ProxyMode.UseSystem:
                        return WebRequest.GetSystemWebProxy().Credentials;
                    case NetworkOptions.ProxyMode.UseCustom:
                        var c = (options.Value.CustomProxies?.FirstOrDefault(x =>
                            x.Id == options.Value.Proxy.CustomProxyId))?.Credentials;
                        return c != null ? new NetworkCredential(c.Username, c.Password, c.Domain) : null;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            set { }
        }
    }
}