using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Components.Network;

/// <summary>Build-time facts shared by routing, validation and generated frontend constants.</summary>
public static class DownloadToolProxyTargets
{
    public const string BaiduPcsGo = "baidu-pcs-go";
    public const string Gdown = "gdown";
    public const string GalleryDl = "gallery-dl";
    public const string Rclone = "rclone";
    public const string MegaCmd = "megacmd";
    public const string PikPakCli = "pikpak-cli";
    public const string TeraBoxCli = "terabox-cli";
    public const string SourceResolver = "sourceResolver";

    public sealed record Target(string Id, string Name, ThirdPartyId? ThirdPartyId,
        IReadOnlyList<string> SupportedProxySchemes);

    public static readonly IReadOnlyList<Target> All = Array.AsReadOnly(new[]
    {
        new Target(BaiduPcsGo, "BaiduPCS-Go", ThirdPartyId.BaiduNetdisk, ["http", "https", "socks5", "socks5h"]),
        new Target(Gdown, "gdown", ThirdPartyId.GoogleDrive, ["http", "https", "socks4", "socks5", "socks5h"]),
        new Target(GalleryDl, "gallery-dl", ThirdPartyId.Gofile, ["http", "https", "socks4", "socks5", "socks5h"]),
        new Target(Rclone, "rclone", null, ["http", "https", "socks5", "socks5h"]),
        new Target(MegaCmd, "MEGAcmd", ThirdPartyId.Mega, ["http", "https"]),
        new Target(PikPakCli, "PikPak CLI", ThirdPartyId.PikPak, ["http", "https", "socks5"]),
        new Target(TeraBoxCli, "TeraBox CLI", ThirdPartyId.TeraBox, ["http", "https", "socks5"]),
        new Target(SourceResolver, "Source link resolver", null, ["http", "https", "socks4", "socks5"])
    });

    public static Target? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
