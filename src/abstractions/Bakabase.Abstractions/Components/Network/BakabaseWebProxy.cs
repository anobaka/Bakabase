using System.Net;
using System;
using System.Linq;
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

        /// <summary>Explicit tool selection, then the calling source, then the global selection.
        /// Missing entries inherit; an explicit direct selection never falls through.</summary>
        public IWebProxy ForDownloadTool(string toolId, ThirdPartyId? thirdPartyId = null)
        {
            var target = DownloadToolProxyTargets.Find(toolId)
                         ?? throw new ArgumentException("Unknown download tool proxy target.", nameof(toolId));
            return new DownloadToolWebProxy(this, options, target, thirdPartyId ?? target.ThirdPartyId);
        }

        internal Uri? Resolve(NetworkOptions.ProxyModel model, Uri destination)
        {
            switch (model.Mode)
            {
                case NetworkOptions.ProxyMode.DoNotUse:
                    return null;
                case NetworkOptions.ProxyMode.UseSystem:
                    var system = HttpClient.DefaultProxy;
                    // Environment proxies apply NO_PROXY only through IsBypassed;
                    // GetProxy alone still returns their configured proxy endpoint.
                    return system.IsBypassed(destination) ? null : system.GetProxy(destination);
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
                    return HttpClient.DefaultProxy.Credentials;
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

        private sealed class DownloadToolWebProxy(BakabaseWebProxy root, IBOptions<NetworkOptions> options,
            DownloadToolProxyTargets.Target target, ThirdPartyId? thirdPartyId) : IWebProxy
        {
            private NetworkOptions.ProxyModel Model
            {
                get
                {
                    var settings = options.Value;
                    if (settings.DownloadToolProxies?.TryGetValue(target.Id, out var tool) == true && tool != null)
                        return tool;
                    if (thirdPartyId is { } site && settings.ThirdPartyProxies?.TryGetValue((int)site, out var source) == true && source != null)
                        return source;
                    return settings.Proxy;
                }
            }

            public Uri? GetProxy(Uri destination)
            {
                var model = Model;
                // A broken explicit/inherited selection is not permission to bypass it.
                if (model.Mode == NetworkOptions.ProxyMode.UseCustom &&
                    !Uri.TryCreate(options.Value.CustomProxies?.FirstOrDefault(p => p.Id == model.CustomProxyId)?.Address,
                        UriKind.Absolute, out _))
                    throw new InvalidOperationException("The selected download-tool proxy is unavailable. Update its network settings.");
                var endpoint = root.Resolve(model, destination);
                if (endpoint != null && endpoint != destination && !target.SupportedProxySchemes.Contains(endpoint.Scheme))
                    throw new InvalidOperationException("The selected proxy protocol is not supported by this download tool.");
                return endpoint;
            }

            public bool IsBypassed(Uri host) => false;
            public ICredentials? Credentials { get => root.ResolveCredentials(Model); set { } }
        }
    }
}
