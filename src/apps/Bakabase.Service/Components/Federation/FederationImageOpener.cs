using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Service.Components.Federation;

/// <summary>Opens an already authorized image on this viewing computer through
/// its default file association. It never accepts a peer's program or a URL.</summary>
public interface IFederationImageOpener
{
    bool Available { get; }
    void Open(string imagePath, CancellationToken ct = default);
}

public sealed class FederationImageOpener(IServerSelfDescription? self = null) : IFederationImageOpener
{
    public bool Available => self?.Kind != ServerKind.Headless &&
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true" &&
        (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
         OperatingSystem.IsLinux() && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
                                     !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))));

    public void Open(string imagePath, CancellationToken ct = default)
    {
        if (!Available) throw new FederationQueryException("PlayerUnavailable", 501,
            "An image viewer cannot be opened on this device. Use the image preview instead.");
        // Repeat the file check immediately before handing it to the OS; a
        // mapping's symlink may have changed since preparation began.
        if (!TryGetImagePath(Path.GetFileName(imagePath), imagePath, out var physical))
            throw new FederationQueryException("UnsupportedPlaybackMode", 422,
                "Only an existing local image can be opened. Use the image preview instead.");
        ct.ThrowIfCancellationRequested();
        try { using var process = Process.Start(new ProcessStartInfo(physical!) {UseShellExecute = true}); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new FederationQueryException("PlayerUnavailable", 501,
                "The default image viewer could not be opened on this device. Use the image preview instead.",
                innerException: ex);
        }
    }

    /// <summary>Both the peer's declared file and the physical local target must
    /// be the same supported image type. A safe-looking link cannot launch an executable.</summary>
    internal static bool TryGetImagePath(string declaredFileName, string? localPath, out string? physicalPath)
    {
        physicalPath = null;
        if (string.IsNullOrWhiteSpace(declaredFileName) || string.IsNullOrWhiteSpace(localPath) ||
            !Path.IsPathFullyQualified(localPath) || !File.Exists(localPath)) return false;
        try
        {
            var declaredExtension = Path.GetExtension(declaredFileName);
            var localExtension = Path.GetExtension(localPath);
            var physical = MediaPathBoundary.ResolvePhysical(localPath);
            var physicalExtension = Path.GetExtension(physical);
            if (!InternalOptions.ImageExtensions.Contains(declaredExtension) ||
                !InternalOptions.ImageExtensions.Contains(localExtension) ||
                !InternalOptions.ImageExtensions.Contains(physicalExtension) ||
                MediaContentTypes.Get(declaredFileName)?.Kind != "image" ||
                MediaContentTypes.Get(physical)?.Kind != "image" ||
                !declaredExtension.Equals(localExtension, StringComparison.OrdinalIgnoreCase) ||
                !declaredExtension.Equals(physicalExtension, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(physical)) return false;
            physicalPath = physical;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
