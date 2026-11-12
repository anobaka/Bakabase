using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.PikPakCli;

/// <summary>Official CLI manifest and checksums, reviewed 2026-10-27. Never follows automatic upstream updates.</summary>
public static class PikPakCliRelease
{
    public const string Version = "0.6.0";
    public sealed record Asset(string Os, string Architecture, string Name, string Sha256)
    {
        public string Url => $"https://download.mypikpak.com/cli/release/v{Version}/{Name}";
    }

    public static IReadOnlyList<Asset> Assets { get; } = Array.AsReadOnly(new[]
    {
        new Asset("macos", "x64", "pikpak_darwin_amd64", "93e15acf9f65fbd6206362609a48cda70a0ee89d85e62ee7445e75be7684625c"),
        new Asset("macos", "arm64", "pikpak_darwin_arm64", "b0df7d96cc544908e6a5584ca81956a6490fbf71d4e003a40d9c02893c2fa559"),
        new Asset("linux", "x64", "pikpak_linux_amd64", "59fcbf237bb6523d96a1470a703ae55a2ec97adbd7ec7fc04b99a20112597495"),
        new Asset("linux", "arm64", "pikpak_linux_arm64", "3c1edae39d884eeb67a8a9a8cddd6687632c8c898cae8151cd0ffcb9affd52fb"),
        new Asset("windows", "x64", "pikpak_windows_amd64.exe", "b39a2bf29d69e561ab3a6f7ec2bfd899930008ed311eb3bd99c3697802e93b04"),
        new Asset("windows", "arm64", "pikpak_windows_arm64.exe", "274bf700a959396b8e588b5d9862fa0484db8f8f8cd4a9c6bccd86e8b1532bd4")
    });

    public static Asset Select(string os, string architecture) => Assets.SingleOrDefault(a =>
        a.Os == os && a.Architecture == architecture) ?? throw new PlatformNotSupportedException("PikPak CLI is not supported on this platform.");

    internal static async Task VerifyAsync(string path, Asset asset, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PikPak CLI release checksum mismatch.");
    }
}
