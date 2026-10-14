using System;
using System.Runtime.InteropServices;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Service.Components.Federation;

public enum VlcLoopbackHttpSupport
{
    Available,
    SystemProxy,
    Unknown
}

public interface IFederationPlayerProxyEnvironment
{
    VlcLoopbackHttpSupport GetVlcLoopbackHttpSupport();
}

/// <summary>Checks configuration locally, without sending a media ticket to a player or proxy.</summary>
public sealed class FederationPlayerProxyEnvironment : IFederationPlayerProxyEnvironment
{
    public VlcLoopbackHttpSupport GetVlcLoopbackHttpSupport()
    {
        // POSIX may invoke a libproxy helper, then falls back to http_proxy without
        // checking no_proxy. Windows reads VLC's private http-proxy preferences.
        // Until those configurations can be verified, do not expose a ticket to VLC.
        if (!OperatingSystem.IsMacOS()) return VlcLoopbackHttpSupport.Unknown;

        IntPtr settings = IntPtr.Zero, proxyKey = IntPtr.Zero, portKey = IntPtr.Zero;
        try
        {
            settings = CFNetworkCopySystemProxySettings();
            proxyKey = CFStringCreateWithCString(IntPtr.Zero, "HTTPProxy", 0x08000100);
            portKey = CFStringCreateWithCString(IntPtr.Zero, "HTTPPort", 0x08000100);
            if (settings == IntPtr.Zero || proxyKey == IntPtr.Zero || portKey == IntPtr.Zero)
                return VlcLoopbackHttpSupport.Unknown;

            // Mirror VLC 3.x src/darwin/netconf.c. It ignores HTTPEnable, no_proxy,
            // ExceptionsList and the destination URL. Even a disabled proxy or a
            // localhost bypass entry cannot make these two configured values safe.
            return CFDictionaryGetValue(settings, proxyKey) != IntPtr.Zero &&
                   CFDictionaryGetValue(settings, portKey) != IntPtr.Zero
                ? VlcLoopbackHttpSupport.SystemProxy
                : VlcLoopbackHttpSupport.Available;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return VlcLoopbackHttpSupport.Unknown;
        }
        finally
        {
            if (portKey != IntPtr.Zero) CFRelease(portKey);
            if (proxyKey != IntPtr.Zero) CFRelease(proxyKey);
            if (settings != IntPtr.Zero) CFRelease(settings);
        }
    }

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport("/System/Library/Frameworks/CFNetwork.framework/CFNetwork")]
    private static extern IntPtr CFNetworkCopySystemProxySettings();

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}

public sealed class FederationPlayerPolicy(LocalPlayerResolver players, IFederationPlayerProxyEnvironment environment)
{
    public ResolvedPlayer Resolve(string fileName, string? localPath, string? playerId = null)
    {
        if (playerId != null)
        {
            if (playerId == "system-default")
                return localPath != null ? ResolvedPlayer.SystemDefault :
                    throw new FederationQueryException("MappedPathUnavailable", 409,
                        "The default application needs an accessible local file. Map the file or choose an HTTP player.");
            try
            {
                var selected = players.ResolveKnown(playerId, fileName, localPath == null);
                if (localPath == null && playerId is not ("Mpv" or "Iina") &&
                    (playerId != "Vlc" || environment.GetVlcLoopbackHttpSupport() != VlcLoopbackHttpSupport.Available))
                    throw new FederationQueryException("PlayerProxyUnsupported", 409);
                return selected;
            }
            catch (InvalidOperationException e)
            { throw new FederationQueryException("PlayerUnavailable", 409, e.Message); }
        }
        if (localPath == null)
        {
            var vlcAllowed = environment.GetVlcLoopbackHttpSupport() == VlcLoopbackHttpSupport.Available;
            // These players have explicit direct-proxy options in FederationPlayerArguments.
            // Do not try VLC first: the ticket would already have reached the system proxy.
            return players.ResolveInstalled(fileName,
                       candidate => candidate == KnownPlayerDefinitions.Mpv || candidate == KnownPlayerDefinitions.Iina ||
                                    vlcAllowed && candidate == KnownPlayerDefinitions.Vlc, requireHttp: true)
                   ?? throw new FederationQueryException(vlcAllowed ? "PlayerUnavailable" : "PlayerProxyUnsupported",
                       vlcAllowed ? 501 : 409,
                       vlcAllowed
                           ? "Install VLC, mpv or IINA to open this stream, configure a local path mapping, or use the preview."
                           : "A direct connection cannot be verified for VLC with the current player and proxy configuration. " +
                             "Use the in-app preview, configure a local path mapping, or install mpv (or IINA on macOS).");
        }

        return players.ResolveInstalled(fileName) ?? throw new FederationQueryException("PlayerUnavailable", 501,
            "Install a supported media player on this device to open this stream.");
    }
}
