using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Bakabase.Abstractions.Helpers;

public static class WindowsWebpViewer
{
    private const string PhotosAppIdPrefix = "Microsoft.Windows.Photos_8wekyb3d8bbwe!";
    private const uint AssociationAppId = 21; // ASSOCSTR_APPID
    private const uint IgnoreUnknownAssociation = 0x400; // ASSOCF_INIT_IGNOREUNKNOWN
    private const uint ProtocolAssociation = 0x1000; // ASSOCF_IS_PROTOCOL

    /// <summary>Opens an existing WebP using the Windows Photos viewer's folder navigation.</summary>
    public static bool TryOpen(string path)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10) || !Path.IsPathFullyQualified(path) ||
            !Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return false;

        var uri = BuildViewerUri(path, ReadAppId(".webp"), ReadAppId("ms-photos", ProtocolAssociation));
        if (uri == null) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri) {UseShellExecute = true});
            return true;
        }
        catch (Win32Exception)
        {
            // The app or its registration may have changed since the association lookup.
            return false;
        }
    }

    internal static string? BuildViewerUri(string path, string? fileAppId, string? protocolAppId)
    {
        // The Photos file association opens a single image. Its viewer URI lets Photos
        // browse the containing folder and must honor the user's default file handler.
        if (fileAppId?.StartsWith(PhotosAppIdPrefix, StringComparison.OrdinalIgnoreCase) != true ||
            protocolAppId?.StartsWith(PhotosAppIdPrefix, StringComparison.OrdinalIgnoreCase) != true)
            return null;

        return $"ms-photos:viewer?fileName={Uri.EscapeDataString(path.Replace('/', '\\'))}";
    }

    private static string? ReadAppId(string association, uint flags = 0)
    {
        var buffer = new StringBuilder(129); // An AppUserModelID is at most 128 characters.
        var length = (uint) buffer.Capacity;
        return AssocQueryStringW(IgnoreUnknownAssociation | flags, AssociationAppId,
            association, null, buffer, ref length) == 0 ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(uint flags, uint associationString, string association,
        string? extra, [Out] StringBuilder output, ref uint outputLength);
}
