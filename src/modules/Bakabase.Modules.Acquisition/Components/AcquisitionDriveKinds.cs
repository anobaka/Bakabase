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
    /// Known sharing hosts. An unrelated HTTP page remains unknown until an explicit file lead or provider resolves it.
    /// </summary>
    private static readonly (string Suffix, AcquisitionDriveKind Kind)[] ByHost =
    [
        ("pan.baidu.com", AcquisitionDriveKind.Baidu),
        ("yun.baidu.com", AcquisitionDriveKind.Baidu),
        ("gofile.io", AcquisitionDriveKind.Gofile),
        ("pixeldrain.com", AcquisitionDriveKind.Pixeldrain),
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

        // Only recognizable file paths are inferred as direct files. Other web pages stay unknown.
        var extension = System.IO.Path.GetExtension(uri.AbsolutePath);
        return new[] {".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".pdf", ".epub", ".mp4", ".mkv", ".mp3", ".flac", ".iso"}
            .Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? AcquisitionDriveKind.DirectUrl : AcquisitionDriveKind.Unknown;
    }

    public static AcquisitionDriveKind InferExplicitDirectLead(string? url)
    {
        var inferred = Infer(url);
        return inferred == AcquisitionDriveKind.Unknown && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" ? AcquisitionDriveKind.DirectUrl : inferred;
    }

    /// <summary>
    /// Whether a link of this kind can be fetched without a person. Everything else goes through
    /// the inbox: cloud drives behind logins and captchas cannot be automated for long.
    /// </summary>
    public static bool CanBeFetchedDirectly(this AcquisitionDriveKind kind) =>
        kind is AcquisitionDriveKind.DirectUrl or AcquisitionDriveKind.Cloudflare;
}
