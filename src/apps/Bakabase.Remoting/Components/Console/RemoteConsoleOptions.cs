using Bakabase.Modules.RemoteAccess.Components.Management;

namespace Bakabase.Remoting.Components.Console;

/// <summary>Desktop relay settings around shared management connection options.</summary>
public sealed class RemoteConsoleOptions : ManagedServerOptions
{
    public const int DefaultFirstRelayPort = 34650;
    public int FirstRelayPort { get; set; } = DefaultFirstRelayPort;
    public int RelayPortRange { get; set; } = 256;
}
