using Bakabase.Infrastructures.Components.Gui;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Bakabase.Service.Components;

public class NullGuiAdapter : IGuiAdapter
{
    public void ShowFatalErrorWindow(string message, string title = "Fatal Error")
    {

    }

    public void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null)
    {

    }

    public void DestroyInitializationWindow()
    {

    }

    public void ShowMainWebView(string url, string title, Func<Task> onClosing)
    {

    }

    public void SetMainWindowTitle(string title)
    {

    }

    public bool MainWebViewVisible { get; } = true;

    public void Shutdown()
    {

    }

    public void Hide()
    {

    }

    public void Show()
    {

    }

    public void ShowConfirmationDialogOnFirstTimeExiting(Func<CloseBehavior, bool, Task> onClosed)
    {

    }

    public bool ShowConfirmDialog(string message, string caption)
    {
        return true;
    }

    public void ChangeUiTheme(UiTheme theme)
    {

    }

    public byte[]? GetIcon(IconType type, string? path)
    {
        return null;
    }

    public Task<string?> CaptureWebViewCookiesAsync(string loginUrl, string title, string[] cookieUrls,
        Dictionary<string, string>? labels = null)
    {
        return Task.FromResult<string?>(null);
    }
}
