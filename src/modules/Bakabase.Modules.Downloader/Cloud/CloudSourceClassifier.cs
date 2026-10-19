namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Recognition does not imply that a provider is installed or a share is accessible.</summary>
public static class CloudSourceClassifier
{
    public static string Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "unknown";
        if (uri.Scheme == "magnet") return "magnet";
        if (uri.Scheme is not ("http" or "https")) return "unknown";
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath;
        if (host is "pan.baidu.com" or "yun.baidu.com" &&
            (path.StartsWith("/s/", StringComparison.Ordinal) || path == "/share/init")) return "baidu";
        if (host == "drive.google.com" && (path.Contains("/folders/", StringComparison.Ordinal) ||
            path.StartsWith("/file/d/", StringComparison.Ordinal) || path is "/uc" or "/open")) return "googleDrive";
        if (host == "docs.google.com" && new[] {"/document/d/", "/spreadsheets/d/", "/presentation/d/"}
            .Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal))) return "googleDrive";
        if (host is "gofile.io" or "www.gofile.io" && path.StartsWith("/d/", StringComparison.Ordinal)) return "gofile";
        if (host is "mypikpak.com" or "www.mypikpak.com" or "pikpak.com") return "pikpak";
        if (host is "1drv.ms" or "onedrive.live.com" || host.EndsWith(".sharepoint.com", StringComparison.Ordinal)) return "oneDrive";
        if (host is "pixeldrain.com" or "www.pixeldrain.com") return "pixeldrain";
        if (host is "mega.nz" or "mega.co.nz") return "mega";
        if (Path.GetExtension(path).Equals(".torrent", StringComparison.OrdinalIgnoreCase)) return "torrent";
        if (new[] {".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".pdf", ".epub", ".mp4", ".mkv", ".mp3", ".flac", ".iso"}
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return "http";
        return "unknown";
    }
}
