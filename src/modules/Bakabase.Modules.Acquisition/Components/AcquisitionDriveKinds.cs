using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Works out which sharing service a link points at, from its host name alone.
/// <para>
/// It decides two things: which link to prefer when a post offers the same files on three drives,
/// and whether a link can be fetched without a person. Getting it wrong is cheap — an unrecognised
/// host is <see cref="AcquisitionDriveKind.Unknown"/>, which means "ask someone", never "guess".
/// </para>
/// </summary>
public static class AcquisitionDriveKinds
{
    /// <summary>
    /// Matched as a host suffix, so <c>pan.baidu.com</c> and <c>yun.baidu.com</c> both hit the
    /// <c>baidu.com</c> entry without either being spelled out.
    /// </summary>
    private static readonly (string Suffix, AcquisitionDriveKind Kind)[] ByHost =
    [
        ("baidu.com", AcquisitionDriveKind.Baidu),
        ("xunlei.com", AcquisitionDriveKind.Xunlei),
        ("feimaoyun.com", AcquisitionDriveKind.Feimao),
        ("fmpan.com", AcquisitionDriveKind.Feimao),
        ("r2.dev", AcquisitionDriveKind.Cloudflare),
        ("workers.dev", AcquisitionDriveKind.Cloudflare),
        ("mega.nz", AcquisitionDriveKind.Mega),
        ("mega.co.nz", AcquisitionDriveKind.Mega),
        ("mypikpak.com", AcquisitionDriveKind.PikPak),
        ("pikpak.com", AcquisitionDriveKind.PikPak),
        ("drive.google.com", AcquisitionDriveKind.GoogleDrive),
        ("1drv.ms", AcquisitionDriveKind.OneDrive),
        ("onedrive.live.com", AcquisitionDriveKind.OneDrive),
        ("sharepoint.com", AcquisitionDriveKind.OneDrive),
    ];

    public static AcquisitionDriveKind Infer(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return AcquisitionDriveKind.Unknown;

        var trimmed = url.Trim();

        if (trimmed.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return AcquisitionDriveKind.Magnet;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return AcquisitionDriveKind.Unknown;
        }

        var host = uri.Host;

        foreach (var (suffix, kind) in ByHost)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        // 115 keeps two names and neither has a usable suffix to match on.
        if (host.Equals("115.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".115.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("anxia.com", StringComparison.OrdinalIgnoreCase))
        {
            return AcquisitionDriveKind.OneOneFive;
        }

        // An http(s) link nothing claimed: it may well be the file itself, which is the one case a
        // fetch step can handle on its own.
        return AcquisitionDriveKind.DirectUrl;
    }

    /// <summary>
    /// Whether a link of this kind can be fetched without a person. Everything else goes through
    /// the inbox: cloud drives behind logins and captchas cannot be automated for long.
    /// </summary>
    public static bool CanBeFetchedDirectly(this AcquisitionDriveKind kind) =>
        kind is AcquisitionDriveKind.DirectUrl or AcquisitionDriveKind.Cloudflare;
}
