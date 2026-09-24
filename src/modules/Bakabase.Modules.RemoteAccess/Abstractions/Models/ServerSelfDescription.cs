namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>
/// What kind of Bakabase an install is, as it tells other devices — for showing, never for
/// deciding anything. Optional wherever it travels (<c>server-info</c>, the discovery beacon,
/// federation's node info): an older install says nothing, and a reader takes a value it does
/// not know for nothing.
/// </summary>
public enum ServerKind
{
    Unknown = 0,

    /// <summary>The desktop app: a window on somebody's computer, which can also manage other servers.</summary>
    Desktop = 1,

    /// <summary>A server with no window of its own — Docker, a NAS — which is only ever managed.</summary>
    Headless = 2
}

/// <summary>What this install says it is, beyond who it is.</summary>
public interface IServerSelfDescription
{
    /// <summary>Null when the host cannot tell.</summary>
    ServerKind? Kind { get; }

    /// <summary>The operating system this process runs on — a container's, in Docker.</summary>
    RemoteDevicePlatform? Platform { get; }
}

/// <summary>
/// The operating system from the process itself; the kind from the host, which is the only one
/// that knows it, and asked only when somebody wants it.
/// </summary>
public sealed class ServerSelfDescription(Func<ServerKind?>? kind = null) : IServerSelfDescription
{
    public ServerKind? Kind => kind?.Invoke();

    public RemoteDevicePlatform? Platform => CurrentPlatform;

    public static RemoteDevicePlatform? CurrentPlatform =>
        OperatingSystem.IsWindows() ? RemoteDevicePlatform.Windows
        : OperatingSystem.IsMacOS() ? RemoteDevicePlatform.MacOS
        : OperatingSystem.IsAndroid() ? RemoteDevicePlatform.Android
        : OperatingSystem.IsIOS() ? RemoteDevicePlatform.IOS
        : OperatingSystem.IsLinux() ? RemoteDevicePlatform.Linux
        : null;
}

/// <summary>
/// The words the kind and the platform travel as where a protocol carries text — the discovery
/// beacon, federation's node info — rather than this build's enum numbers, which a later build
/// may extend. Reading is lenient both ways: what is absent or not known is null, never an error.
/// </summary>
public static class ServerSelfDescriptionWords
{
    /// <summary>The longest a word may be on the wire: longer is not one of ours.</summary>
    public const int MaxLength = 32;

    public static string? Of(ServerKind? kind) => kind switch
    {
        ServerKind.Desktop => "desktop",
        ServerKind.Headless => "headless",
        _ => null
    };

    public static string? Of(RemoteDevicePlatform? platform) => platform switch
    {
        RemoteDevicePlatform.Windows => "windows",
        RemoteDevicePlatform.MacOS => "macos",
        RemoteDevicePlatform.Linux => "linux",
        RemoteDevicePlatform.Android => "android",
        RemoteDevicePlatform.IOS => "ios",
        _ => null
    };

    public static ServerKind? KindOf(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "desktop" => ServerKind.Desktop,
        "headless" => ServerKind.Headless,
        _ => null
    };

    public static RemoteDevicePlatform? PlatformOf(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "windows" => RemoteDevicePlatform.Windows,
        "macos" => RemoteDevicePlatform.MacOS,
        "linux" => RemoteDevicePlatform.Linux,
        "android" => RemoteDevicePlatform.Android,
        "ios" => RemoteDevicePlatform.IOS,
        _ => null
    };

    /// <summary>
    /// Only the values this build defines, whatever number a peer sent: an unknown one reads as
    /// nothing rather than as a value that means something else.
    /// </summary>
    public static ServerKind? Known(ServerKind? kind) =>
        kind is ServerKind.Desktop or ServerKind.Headless ? kind : null;

    public static RemoteDevicePlatform? Known(RemoteDevicePlatform? platform) =>
        platform is { } value && value != RemoteDevicePlatform.Unknown && Enum.IsDefined(value) ? value : null;
}
