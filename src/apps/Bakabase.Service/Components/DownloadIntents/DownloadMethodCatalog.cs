using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.CloudDownloads.Rclone;

namespace Bakabase.Service.Components.DownloadIntents;

/// <summary>A source capability is separate from its execution engine and account prerequisites.</summary>
public sealed record DownloadMethodDescriptor(string Id, string ProviderId, string DisplayName,
    string Transport, IReadOnlyList<string> DependencyIds, string? AccountProvider = null,
    bool SupportsTransfer = false, bool SupportsRemote = false,
    Func<CloudSource, DownloadRequirement?>? ValidateSource = null,
    string? EngineId = null, string? Scope = null, bool RemoteOnly = false, string? ArtifactScope = null,
    DownloadMethodAvailabilityConstraint? Availability = null)
{
    public IReadOnlyList<string> SourceKinds => RemoteOnly ? ["remote"] : SupportsRemote ? ["share", "remote"] : ["share"];
}

/// <summary>A temporary integration limitation, independent of a share's validity or account setup.</summary>
public sealed record DownloadMethodAvailabilityConstraint(string Status, string DescriptionKey, string Description)
{
    public const string ReasonCode = "DOWNLOAD_METHOD_UNAVAILABLE";
    public DownloadRequirement Requirement() => new(ReasonCode, "capability", Description,
        Actions: ["chooseSource", "manualDownload"]);
}

public static class DownloadMethodCatalog
{
    public static readonly IReadOnlyList<DownloadMethodDescriptor> Methods =
    [
        new("baidu.pcs", "baidu", "BaiduPCS-Go", "site", ["baidu-pcs-go-component-service"], "baidu", true, true),
        new("googleDrive.gdown", "googleDrive", "gdown", "site", ["gdown-component-service"],
            ValidateSource: source => Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.IdnHost == "docs.google.com"
                ? new("NATIVE_DOCUMENT_EXPORT_UNSUPPORTED", "source",
                    "The current Google Drive adapter downloads files and folders; native Docs, Sheets and Slides export requires manual handling.", Actions: ["manualDownload"]) : null),
        new("gofile.galleryDl", "gofile", "gallery-dl", "site", ["gallery-dl-component-service"]),
        new("pixeldrain.galleryDl", "pixeldrain", "gallery-dl", "site", ["gallery-dl-component-service"],
            ValidateSource: source => RequireHttps(source) ??
                (Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && Regex.IsMatch(uri.AbsolutePath, @"^/(u|l)/[^/]+/?$")
                    ? null : new("PIXELDRAIN_SHARE_REQUIRED", "source", "This method supports Pixeldrain /u/ file and /l/ list shares.", Actions: ["manualDownload"])),
            EngineId: "gallery-dl", Scope: "publicShare"),
        new("pixeldrain.rclone", "pixeldrain", "rclone", "site", ["rclone-component-service"],
            ValidateSource: source => RequireHttps(source) ??
                (PixeldrainRcloneCloudProvider.CanHandle(source.Url) && string.IsNullOrEmpty(source.AccessCode)
                    ? null : new("PIXELDRAIN_DIRECTORY_REQUIRED", "source", "rclone supports Pixeldrain Filesystem /d/ directories. /u/ and /l/ shares use the public-share method.", Actions: ["manualDownload"])),
            EngineId: "rclone", Scope: "publicDirectory", ArtifactScope: "pixeldrain.rclone:v1"),
        new("pixeldrain.rcloneRemote", "pixeldrain", "rclone", "site", ["rclone-component-service"], "pixeldrain",
            SupportsRemote: true, EngineId: "rclone", Scope: "accountRemote", RemoteOnly: true, ArtifactScope: "pixeldrain.rcloneRemote:v1"),
        new("pikpak.share", "pikpak", "PikPak CLI", "site", ["pikpak-cli-component-service"], "pikpakCli",
            ValidateSource: source => RequireHttps(source), EngineId: "pikpak-cli", Scope: "publicShare"),
        new("terabox.cli", "terabox", "TeraBox CLI", "site", ["terabox-cli-component-service"], "terabox",
            ValidateSource: source => TeraBoxShareLocator.TryParse(source.Url, out _) ? null :
                new("TERABOX_SOURCE_INVALID", "source", "Provide a complete TeraBox HTTPS public share link."),
            EngineId: "terabox-cli", Scope: "publicShare", ArtifactScope: "terabox.cli:v1",
            Availability: new("developing", "cloudDownload.teraboxIntegrationUnavailable",
                "The current TeraBox download integration is temporarily unavailable while OAuth and download interface compatibility is being investigated. This does not mean the share has expired. Keep the account configuration and choose another source or download manually.")),
        new("mega.megacmd", "mega", "MEGAcmd", "site", ["megacmd-component-service"],
            ValidateSource: source => CloudDownloads.Mega.MegaPublicLink.IsValid(source.Url, source.AccessCode) ? null :
                new("MEGA_SHARE_INVALID", "source", "Provide a complete MEGA public file or folder link, including its decryption key."),
            EngineId: "megacmd", Scope: "publicShare", ArtifactScope: "mega.megacmd:v1"),
        new("pikpak.rclone", "pikpak", "rclone", "site", ["rclone-component-service"], "pikpak",
            SupportsRemote: true, EngineId: "rclone", Scope: "accountRemote", RemoteOnly: true, ArtifactScope: "pikpak.rclone:v1"),
        new("oneDrive.sharePoint", "oneDrive", "SharePoint public share", "site", [],
            ValidateSource: source => RequireHttps(source) ??
                (!SharePointCloudProvider.SupportsPublicLink(source.Url)
                    ? new("SHAREPOINT_FOLDER_REQUIRED", "source",
                        "Choose a public SharePoint folder link or a shared attachment from a SharePoint personal site.", Actions: ["manualDownload"])
                    : null)),
        new("oneDrive.rclone", "oneDrive", "rclone", "site", ["rclone-component-service"], "oneDrive",
            SupportsRemote: true, EngineId: "rclone", Scope: "accountRemote", RemoteOnly: true, ArtifactScope: "oneDrive.rclone:v1"),
        new("http.native", "http", "Built-in HTTP", "http", []),
        new("bt.native", "magnet", "Built-in BitTorrent", "bt", []),
        new("bt.native", "torrent", "Built-in BitTorrent", "bt", []),
        new("site.exHentai", "exHentai", "ExHentai downloader", "site", [], "exHentai")
    ];

    public static DownloadMethodDescriptor? Find(string providerId) => Methods.FirstOrDefault(m => m.ProviderId == providerId);

    public static DownloadMethodDescriptor? Find(string providerId, string? methodId) =>
        methodId == null ? Find(providerId) : Methods.FirstOrDefault(m => m.ProviderId == providerId && m.Id == methodId);

    public static IReadOnlyList<DownloadMethodDescriptor> FindAll(CloudSource source) => Methods
        .Where(m => m.ProviderId == source.ProviderId && (source.Kind == "remote" ? m.SupportsRemote : !m.RemoteOnly)).ToList();

    public static DownloadMethodDescriptor? Select(CloudSource source) => source.MethodId != null
        ? FindAll(source).FirstOrDefault(m => m.Id == source.MethodId)
        : FindAll(source).FirstOrDefault(m => m.ValidateSource?.Invoke(source) == null) ?? FindAll(source).FirstOrDefault();

    private static DownloadRequirement? RequireHttps(CloudSource source) =>
        Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort
            ? null
            : new("SOURCE_INVALID", "source", "This source requires a standard HTTPS share URL.");
}
