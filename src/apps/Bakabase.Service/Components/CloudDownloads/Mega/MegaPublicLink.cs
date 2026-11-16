using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Mega;

public static class MegaPublicLink
{
    public static bool IsValid(string url, string? accessCode = null) =>
        MegaPublicShare.Parse(url, accessCode)?.DownloadUrl != null;
    public static string Require(string url, string? accessCode = null) =>
        MegaPublicShare.Parse(url, accessCode)?.DownloadUrl ?? throw new CloudProviderException("MEGA_SHARE_INVALID");
}
