using System;
using System.Drawing;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service;

public class Program
{
    public class NullGuiAdapter : IGuiAdapter
    {
        public string[] OpenFilesSelector(string? initialDirectory = null)
        {
            return null;
        }

        public string? OpenFileSelector(string? initialDirectory = null)
        {
            return null;
        }

        public string? OpenFolderSelector(string? initialDirectory = null)
        {
            return null;
        }

        public string GetDownloadsDirectory()
        {
            return null;
        }

        public void ShowTray(Func<Task> onExiting)
        {
            
        }

        public void HideTray()
        {
            
        }

        public void SetTrayText(string text)
        {
            
        }

        public void SetTrayIcon(Icon icon)
        {
            
        }

        public void ShowFatalErrorWindow(string message, string title = "Fatal Error")
        {
            
        }

        public void ShowInitializationWindow(string processName)
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

        public bool MainWebViewVisible { get; }
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
    }

    public class NullSystemService : ISystemService
    {
        public UiTheme UiTheme { get; }
        public string Language { get; }
        public event Func<UiTheme, Task>? OnUiThemeChange;
    }

    public static async Task Main(string[] args)
    {
        var host = new InsideWorldHost(new NullGuiAdapter(), new NullSystemService());
        await host.Start(args);
        while (true)
        {
            await Task.Delay(TimeSpan.FromDays(1));
        }
    }
}