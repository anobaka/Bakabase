using Bakabase.Infrastructures.Components.Gui;
using System.Threading.Tasks;
using System;

namespace Bakabase.Service.Components;

public class NullGuiAdapter(Action? onReady = null, Action<string>? onFatalError = null) : IGuiAdapter
{
    public void ShowFatalErrorWindow(string message, string title = "Fatal Error")
    {
        Console.Error.WriteLine($"{title}: {message}");
        onFatalError?.Invoke("The server could not finish starting. See the server log for details.");
    }

    public void ShowInitializationWindow(string processName, string? detail = null, double? fraction = null)
    {

    }

    public void DestroyInitializationWindow()
    {

    }

    public void ShowMainWebView(string url, string title, Func<Task> onClosing)
    {
        Console.WriteLine($"Server ready: {url}");
        onReady?.Invoke();
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

    public IWebViewSession CreateWebViewSession(WebViewSessionOptions options) =>
        CancelledWebViewSession.Instance;
}
