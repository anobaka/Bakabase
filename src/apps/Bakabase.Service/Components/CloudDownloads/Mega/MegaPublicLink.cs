using System;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Mega;

public static class MegaPublicLink
{
    public static bool IsValid(string url, string? accessCode = null) => Resolve(url, accessCode) != null;

    private static string? Resolve(string url, string? accessCode)
    {
        // Uri normalizes some whitespace/control characters. The original string is passed
        // through MEGAcmd's text command protocol, so validate it before that normalization.
        if (string.IsNullOrWhiteSpace(url) || url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '\'' or '"' or '\\')) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Query.Length != 0 ||
            uri.IdnHost is not ("mega.nz" or "mega.co.nz" or "www.mega.nz") || url.Contains('\\')) return null;
        if (Regex.IsMatch(uri.AbsolutePath + uri.Fragment,
            @"\A/(?:file/[A-Za-z0-9_-]{8}\#[A-Za-z0-9_-]{43}|folder/[A-Za-z0-9_-]{8}\#[A-Za-z0-9_-]{22}(?:/(?:file|folder)/[A-Za-z0-9_-]{8})?|\#![A-Za-z0-9_-]{8}![A-Za-z0-9_-]{43}|\#F![A-Za-z0-9_-]{8}![A-Za-z0-9_-]{22})\z")) return url;

        // Some publishers provide the public URL and decryption key separately. Build only
        // the CLI argument here: the saved source and its operation identity remain unchanged.
        // An explicit fragment (even invalid or empty) must never be replaced by another key.
        if (url.Contains('#') || accessCode == null) return null;
        var file = Regex.IsMatch(uri.AbsolutePath, @"\A/file/[A-Za-z0-9_-]{8}\z");
        var folder = Regex.IsMatch(uri.AbsolutePath, @"\A/folder/[A-Za-z0-9_-]{8}\z");
        if (!(file && Regex.IsMatch(accessCode, @"\A[A-Za-z0-9_-]{43}\z") ||
              folder && Regex.IsMatch(accessCode, @"\A[A-Za-z0-9_-]{22}\z"))) return null;
        return url + "#" + accessCode;
    }

    public static string Require(string url, string? accessCode = null) =>
        Resolve(url, accessCode) ?? throw new CloudProviderException("MEGA_SHARE_INVALID");
}
