using System;
using System.Drawing;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;

namespace Bakabase.Tests.Implementations;

public class TestGuiAdapter : IGuiAdapter
{
    public string[] OpenFilesSelector(string? initialDirectory = null)
    {
        throw new NotImplementedException();
    }

    public string? OpenFileSelector(string? initialDirectory = null)
    {
        throw new NotImplementedException();
    }

    public string? OpenFolderSelector(string? initialDirectory = null)
    {
        throw new NotImplementedException();
    }

    public string GetDownloadsDirectory()
    {
        throw new NotImplementedException();
    }

    public void ShowTray(Func<Task> onExiting)
    {
        throw new NotImplementedException();
    }

    public void HideTray()
    {
        throw new NotImplementedException();
    }

    public void SetTrayText(string text)
    {
        throw new NotImplementedException();
    }

    public void SetTrayIcon(Icon icon)
    {
        throw new NotImplementedException();
    }

    public void ShowFatalErrorWindow(string message, string title = "Fatal Error")
    {
        throw new NotImplementedException();
    }

    public void ShowInitializationWindow(string processName)
    {
        throw new NotImplementedException();
    }

    public void DestroyInitializationWindow()
    {
        throw new NotImplementedException();
    }

    public void ShowMainWebView(string url, string title, Func<Task> onClosing)
    {
        throw new NotImplementedException();
    }

    public void SetMainWindowTitle(string title)
    {
        throw new NotImplementedException();
    }

    public bool MainWebViewVisible { get; }
    public void Shutdown()
    {
        throw new NotImplementedException();
    }

    public void Hide()
    {
        throw new NotImplementedException();
    }

    public void Show()
    {
        throw new NotImplementedException();
    }

    public void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed)
    {
        throw new NotImplementedException();
    }

    public bool ShowConfirmDialog(string message, string caption)
    {
        throw new NotImplementedException();
    }

    public void ChangeUiTheme(UiTheme theme)
    {
        throw new NotImplementedException();
    }

    public byte[]? GetIcon(IconType type, string? path)
    {
        throw new NotImplementedException();
    }
}