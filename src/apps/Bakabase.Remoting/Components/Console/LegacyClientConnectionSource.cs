using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Connection;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// The removed thin client's pairings on this machine, read where an old install left them.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, always. The thin client may still be installed and even running, and its
/// file is its own: this reads a copy of the keys and never writes, moves or deletes
/// anything there. A user who goes back to it finds it exactly as it was.
/// </para>
/// <para>
/// Every failure reads as "nothing there": a missing, unreadable or corrupt file must not
/// stop the app from starting, and there is nothing a user could do about one anyway.
/// </para>
/// </remarks>
public sealed class LegacyClientConnectionSource(Func<string?> resolveFile)
{
    /// <summary>The thin client's own name for its subdirectory of AppData.</summary>
    public const string ClientDirectoryName = "client";

    /// <summary>The thin client's executable name, which its debug-build AppData folder is named after.</summary>
    public const string ClientExecutableName = "Bakabase.Client";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = {new JsonStringEnumConverter()}
    };

    /// <summary>Where the file is expected, whether or not it exists. Null when that cannot be worked out.</summary>
    public string? FilePath
    {
        get
        {
            try
            {
                return resolveFile();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>The thin client's pairings, or null when there is no readable file.</summary>
    public ClientConnectionData? Read()
    {
        var path = FilePath;

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Shared read: the thin client may be running and holding the file open.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return JsonSerializer.Deserialize<ClientConnectionData>(stream, ReadOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                      or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where the thin client kept <c>connection.json</c> on this machine: its AppData anchor
    /// by the client profile, then the anchor's redirect when the user moved its data, then
    /// <c>client/connection.json</c> under that.
    /// </summary>
    /// <remarks>
    /// The same rules the thin client resolved at its own start, restated rather than
    /// shared because this process runs under the all-in-one profile and the shared code
    /// answers for the profile in force. Two details follow it exactly: an explicit
    /// <c>BAKABASE_CLIENT_DATA_DIR</c> wins over everything, the redirect included, as it
    /// did there; and a debug build reads a debug client's folder, since that is the only
    /// thin client a developer's debug app could have been used alongside.
    /// </remarks>
    public static string? ResolveDefaultFile()
    {
        bool isDebug;
#if DEBUG
        isDebug = true;
#else
        isDebug = false;
#endif

        return ResolveFile(CurrentPlatform(), Environment.GetEnvironmentVariable, Environment.GetFolderPath,
            isDebug);
    }

    /// <summary>
    /// <see cref="ResolveDefaultFile"/> for a given platform, environment and build, so each
    /// combination can be checked on any machine.
    /// </summary>
    /// <remarks>
    /// Per platform, as <see cref="DefaultAppDataPathResolver"/> lays the client profile out:
    /// <c>%LOCALAPPDATA%\Bakabase.Client.AppData</c> on Windows,
    /// <c>~/Library/Application Support/Bakabase.Client</c> on macOS, and
    /// <c>$XDG_DATA_HOME/Bakabase.Client</c> (default <c>~/.local/share</c>) on Linux; a debug
    /// build <c>{ApplicationData}/Bakabase.Client.Debugging</c> everywhere. The anchor's
    /// <c>.redirect</c> is followed exactly as <see cref="EffectiveAppDataResolver"/> follows it
    /// for the thin client itself. Reads only: a missing directory is simply a path with no
    /// file at the end of it.
    /// </remarks>
    public static string? ResolveFile(OSPlatform platform, Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolder, bool isDebug)
    {
        var profile = AppDataPathProfile.Client;

        string anchor;
        try
        {
            anchor = DefaultAppDataPathResolver.Resolve(profile, platform, getEnvironmentVariable, getFolder,
                ClientExecutableName, isDebug);
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException)
        {
            // A relative BAKABASE_CLIENT_DATA_DIR stopped the thin client from starting at all;
            // it has nothing anywhere.
            return null;
        }

        var dataDirectory = anchor;

        if (string.IsNullOrWhiteSpace(getEnvironmentVariable(profile.EnvVarName)))
        {
            try
            {
                dataDirectory = EffectiveAppDataResolver.Resolve(anchor).DataDir;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A broken redirect stopped the thin client at startup too; nothing it
                // could have written lives anywhere else.
                dataDirectory = anchor;
            }
        }

        return Path.Combine(dataDirectory, ClientDirectoryName, ClientConnectionStore.FileName);
    }

    private static OSPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        throw new PlatformNotSupportedException();
    }
}
