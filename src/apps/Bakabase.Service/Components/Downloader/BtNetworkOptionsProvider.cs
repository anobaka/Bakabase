using System;
using System.Linq;
using System.Net;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Downloader;

public sealed class BtNetworkOptionsProvider(IBOptions<NetworkOptions> options) : ITorrentNetworkOptionsProvider
{
    public TorrentNetworkOptions GetOptions()
    {
        var settings = options.Value;
        var selection = settings.BtProxy;
        if (selection == null || selection.Mode == NetworkOptions.ProxyMode.DoNotUse) return new();
        if (selection.Mode != NetworkOptions.ProxyMode.UseCustom)
            throw new InvalidOperationException("BT requires direct connections or a saved SOCKS5 proxy.");
        var proxy = settings.CustomProxies?.SingleOrDefault(p => p.Id == selection.CustomProxyId)
            ?? throw new InvalidOperationException("The selected BT proxy is unavailable. Choose a proxy or direct connections.");
        if (!Uri.TryCreate(proxy.Address, UriKind.Absolute, out var address) || address.Scheme != "socks5" ||
            !string.IsNullOrEmpty(address.UserInfo))
            throw new InvalidOperationException("The selected BT proxy must use SOCKS5 with separately configured credentials.");
        return new TorrentNetworkOptions
        {
            ProxyUri = address,
            Credentials = proxy.Credentials == null ? null : new NetworkCredential(
                proxy.Credentials.Username, proxy.Credentials.Password, proxy.Credentials.Domain)
        };
    }
}
