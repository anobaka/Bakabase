using System.Runtime.Versioning;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Win32;
using Velopack.Locators;

namespace Bakabase.App;

/// <summary>Per-user Windows association, maintained by install/update/uninstall hooks.</summary>
internal static class DesktopProtocolRegistration
{
    private const string KeyPath = @"Software\Classes\bakabase";

    internal static string Command(string executable)
    {
        if (executable.Contains('"') || executable.Contains('\r') || executable.Contains('\n'))
            throw new ArgumentException("Invalid desktop executable path.", nameof(executable));
        return $"\"{executable}\" {DesktopToolLink.Argument} \"%1\"";
    }

    [SupportedOSPlatform("windows")]
    internal static void Register()
    {
        var locator = VelopackLocator.Current;
        // A portable/dev copy must not take the installed application's association.
        if (locator.IsPortable || locator.CurrentlyInstalledVersion == null || Environment.ProcessPath is not { } executable)
            return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("", "URL:Bakabase desktop tools");
            key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{executable}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", Command(executable));
        }
        catch (Exception error) { Console.Error.WriteLine($"Could not register Bakabase desktop links: {error.Message}"); }
    }

    [SupportedOSPlatform("windows")]
    internal static void Unregister()
    {
        if (Environment.ProcessPath is not { } executable) return;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
            using (var command = key?.OpenSubKey(@"shell\open\command"))
                // Uninstalling a different copy must not remove the current owner's registration.
                if (!string.Equals(command?.GetValue("") as string, Command(executable), StringComparison.OrdinalIgnoreCase)) return;
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception error) { Console.Error.WriteLine($"Could not unregister Bakabase desktop links: {error.Message}"); }
    }
}
