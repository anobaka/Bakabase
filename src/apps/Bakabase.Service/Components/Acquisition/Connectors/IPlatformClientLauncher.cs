using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;

namespace Bakabase.Service.Components.Acquisition.Connectors;

/// <summary>The host's ability to hand a protocol URL to a local desktop application.</summary>
public interface IPlatformClientLauncher
{
    bool IsAvailable { get; }
    void Open(string url);
}

/// <summary>Desktop hosts supply a GuiAdapter; the standalone service supplies NullGuiAdapter.</summary>
public sealed class PlatformClientLauncher(IGuiAdapter gui) : IPlatformClientLauncher
{
    public bool IsAvailable => gui is GuiAdapter;

    public void Open(string url)
    {
        if (!IsAvailable)
            throw new System.InvalidOperationException("This host cannot launch a local desktop application.");
        OsShell.Open(url, false);
    }
}
