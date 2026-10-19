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
        if (selection.Mode == NetworkOptions.ProxyMode.UseSystem) return new() {UseSystemProxy = true};
        if (selection.Mode != NetworkOptions.ProxyMode.UseCustom)
            throw new InvalidOperationException("The BT proxy mode is invalid.");
        var proxy = settings.CustomProxies?.SingleOrDefault(p => p.Id == selection.CustomProxyId)
            ?? throw new InvalidOperationException("The selected BT proxy is unavailable. Choose a proxy or direct connections.");
        if (!Uri.TryCreate(proxy.Address, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "socks5") ||
            string.IsNullOrWhiteSpace(address.Host) || address.Port is < 1 or > 65535 || !string.IsNullOrEmpty(address.UserInfo))
            throw new InvalidOperationException("The selected BT proxy must use HTTP CONNECT or SOCKS5 with a host, port and separately configured credentials.");
        return new TorrentNetworkOptions
        {
            ProxyUri = address,
            Credentials = proxy.Credentials == null ? null : new NetworkCredential(
                proxy.Credentials.Username, proxy.Credentials.Password, proxy.Credentials.Domain)
        };
    }
}
