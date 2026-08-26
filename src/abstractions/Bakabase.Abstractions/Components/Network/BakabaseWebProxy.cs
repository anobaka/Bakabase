using System.Net;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Components.Network
{
    public class BakabaseWebProxy(IBOptions<NetworkOptions> options) : IWebProxy
    {
        public Uri? GetProxy(Uri destination) => Resolve(options.Value.Proxy, destination);

        public bool IsBypassed(Uri host) => false;

        public ICredentials? Credentials
        {
            get => ResolveCredentials(options.Value.Proxy);
            set { }
        }

        /// <summary>
        /// A proxy that honours <paramref name="thirdPartyId"/>'s override, falling back to the global
        /// setting when it has none.
        /// </summary>
        /// <remarks>
        /// <see cref="IWebProxy.GetProxy"/> only receives the destination, so a single shared instance
        /// cannot tell which source is asking. Each source has its own message handler, so the identity
        /// is bound here instead, when the handler is built.
        /// </remarks>
        public IWebProxy ForThirdParty(ThirdPartyId thirdPartyId) =>
            new ThirdPartyWebProxy(this, options, thirdPartyId);

        internal Uri? Resolve(NetworkOptions.ProxyModel model, Uri destination)
        {
            switch (model.Mode)
            {
                case NetworkOptions.ProxyMode.DoNotUse:
                    return null;
                case NetworkOptions.ProxyMode.UseSystem:
                    return WebRequest.GetSystemWebProxy().GetProxy(destination);
                case NetworkOptions.ProxyMode.UseCustom:
                    var p = options.Value.CustomProxies?.FirstOrDefault(x => x.Id == model.CustomProxyId);
                    return !string.IsNullOrEmpty(p?.Address) ? new Uri(p.Address) : null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal ICredentials? ResolveCredentials(NetworkOptions.ProxyModel model)
        {
            switch (model.Mode)
            {
                case NetworkOptions.ProxyMode.DoNotUse:
                    return null;
                case NetworkOptions.ProxyMode.UseSystem:
                    return WebRequest.GetSystemWebProxy().Credentials;
                case NetworkOptions.ProxyMode.UseCustom:
                    var c = options.Value.CustomProxies?.FirstOrDefault(x => x.Id == model.CustomProxyId)
                        ?.Credentials;
                    return c != null ? new NetworkCredential(c.Username, c.Password, c.Domain) : null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private sealed class ThirdPartyWebProxy(
            BakabaseWebProxy root,
            IBOptions<NetworkOptions> options,
            ThirdPartyId thirdPartyId) : IWebProxy
        {
            // Read per call rather than captured, so changing the configuration takes effect without
            // rebuilding the handler (which is kept alive for 30 days by design).
            private NetworkOptions.ProxyModel Model
            {
                get
                {
                    var overrides = options.Value.ThirdPartyProxies;

                    return overrides != null && overrides.TryGetValue((int) thirdPartyId, out var m) && m != null
                        ? m
                        : options.Value.Proxy;
                }
            }

            public Uri? GetProxy(Uri destination) => root.Resolve(Model, destination);

            public bool IsBypassed(Uri host) => false;

            public ICredentials? Credentials
            {
                get => root.ResolveCredentials(Model);
                set { }
            }
        }
    }
}
