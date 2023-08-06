using System.ComponentModel;
using System.Reflection;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Services.Bootstraps;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Bakabase.InsideWorld.App.WinForms
{
    public partial class MainWindow : Form
    {
        private IHost? _host;
        public NotifyIcon? TrayIcon { private set; get; }
        private bool _forceExit;
        private readonly string _appExecutableDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        private readonly SemaphoreSlim _semaphore = new(0, 1);

        // public MainWindow()
        // {
        //     InitializeComponent();
        //
        //     Load += Loaded!;
        //
        //     this.ClientSizeChanged += (sender, args) => { webView21.Size = this.ClientSize; };
        //
        //     this.Closing += OnClose!;
        //
        //     this.Text = $"Bakabase.InsideWorld.App - {AppService.CoreVersion}";
        //
        //     this.Icon = new Icon(File.OpenRead(Path.Combine(_appExecutableDir, "Assets", "favicon.ico")), 16, 16);
        //
        //     this.ShowTrayIcon();
        // }

//         private async void Loaded(object sender, EventArgs args)
//         {
//             this.Hide();
//             this.Visible = false;
//             this.Opacity = 0;
//
//             webView21.Size = ClientSize;
//
//             this.webView21.CoreWebView2InitializationCompleted += (o, eventArgs) =>
//             {
//                 Log.Logger.Information("WebView2 initialized");
//                 _semaphore.Release();
//             };
//             Log.Logger.Information("Initializing WebView2");
// #if DEBUG
//             await this.webView21.EnsureCoreWebView2Async();
// #else
//             var webview2EnvPath =
//                 Path.Combine(_appExecutableDir, "Microsoft.WebView2.FixedVersionRuntime.101.0.1210.53.x64");
//             var env = await CoreWebView2Environment.CreateAsync(webview2EnvPath);
//             await this.webView21.EnsureCoreWebView2Async(env);
// #endif
//             this.webView21.CoreWebView2.Navigate(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
//                 "index.html"));
//         }

//         public async Task OnHostStarted(IHost host)
//         {
//             Log.Logger.Information("Host Started");
//
//             _host = host;
// #if DEBUG
//             var address = "http://localhost:4444";
// #else
//             var server = _host?.Services.GetRequiredService<IServer>();
//             var features = server.Features;
//             var address = features.Get<IServerAddressesFeature>()!
//                 .Addresses.FirstOrDefault();
// #endif
//             await _semaphore.WaitAsync();
//             webView21.BeginInvoke(() =>
//             {
//                 Log.Logger.Information($"Go to {address}");
//                 webView21.CoreWebView2.Navigate(address);
//             });
//         }
        //
        // public async Task OnHostFailedToStart(string message)
        // {
        //     var html = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
        //         "error.html"));
        //     html = html.Replace("{message}", message);
        //
        //     await _semaphore.WaitAsync();
        //     webView21.BeginInvoke(() =>
        //     {
        //         Log.Logger.Information($"Go to {html}");
        //
        //         webView21.NavigateToString(html);
        //     });
        // }

        #region App Exit

        private bool ConfirmExitIfCriticalTasksAreNotCompleted()
        {
            var taskManager = _host?.Services.GetRequiredService<BackgroundTaskManager>();
            var tasks = taskManager?.Tasks;
            if (tasks?.Any(t =>
                    t.Status == BackgroundTaskStatus.Running &&
                    t.Level == BackgroundTaskLevel.Critical) == true)
            {
                var result =
                    MessageBox.Show(
                        "Some critical tasks are not completed yet, data would be lost by forcing shutdown the app. Are you sure to exit now?",
                        "Critical tasks are running", MessageBoxButtons.OKCancel);
                if (result != DialogResult.OK)
                {
                    return false;
                }
            }

            return true;
        }
        //
        // private void ShowTrayIcon()
        // {
        //     if (TrayIcon == null)
        //     {
        //         this.TrayIcon = new NotifyIcon()
        //         {
        //             Icon = new Icon("Assets/favicon.ico"),
        //             ContextMenuStrip = new ContextMenuStrip(),
        //             Visible = true
        //         };
        //
        //         this.TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        //         {
        //             new ToolStripMenuItem("Open", null, (o, eventArgs) =>
        //             {
        //                 this.Show();
        //                 this.TrayIcon!.Visible = false;
        //             }, "Open"),
        //             new ToolStripMenuItem("Exit", null,
        //                 (o, eventArgs) => { this.Close(); },
        //                 "Exit")
        //         });
        //     }
        //     else
        //     {
        //         this.TrayIcon.Visible = true;
        //     }
        // }

        private async void OnClose(object sender, CancelEventArgs args)
        {
            if (_host != null)
            {
                if (_forceExit)
                {
                    return;
                }

                if (TrayIcon?.Visible == true)
                {
                    if (!ConfirmExitIfCriticalTasksAreNotCompleted())
                    {
                        args.Cancel = true;
                        return;
                    }
                }
                else
                {
                    var appOptions = await InsideWorldAppService.GetOptionsAsync();
                    if (!appOptions.MinimizeOnClose.HasValue)
                    {
                        var center = new Point(this.Location.X + this.Size.Width / 2,
                            this.Location.Y + this.Size.Height / 2);
                        var firstTimeExitDialog = new FirstTimeExitDialog();
                        firstTimeExitDialog.Show();
                        firstTimeExitDialog.Location = new Point(center.X - firstTimeExitDialog.Size.Width / 2,
                            center.Y - firstTimeExitDialog.Size.Height / 2);
                        args.Cancel = true;
                        firstTimeExitDialog.OnOperate += (operation, remember) =>
                        {
                            firstTimeExitDialog.Close();
                            if (remember)
                            {
                                AppService.SaveBaseOptionsAsync(t =>
                                    t.MinimizeOnClose = operation == FirstTimeExitDialog.OperationType.Minimize);
                            }

                            switch (operation)
                            {
                                case FirstTimeExitDialog.OperationType.Minimize:
                                {
                                    this.Hide();
                                    break;
                                }
                                case FirstTimeExitDialog.OperationType.Exit:
                                    if (ConfirmExitIfCriticalTasksAreNotCompleted())
                                    {
                                        _forceExit = true;
                                        this.Close();
                                    }

                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
                            }
                        };
                    }
                    else
                    {
                        if (appOptions.MinimizeOnClose.Value)
                        {
                            this.Hide();
                            args.Cancel = true;
                            return;
                        }
                        else
                        {
                            args.Cancel = !ConfirmExitIfCriticalTasksAreNotCompleted();
                        }
                    }
                }

                if (!args.Cancel)
                {

                }
            }
        }

        #endregion
    }
}