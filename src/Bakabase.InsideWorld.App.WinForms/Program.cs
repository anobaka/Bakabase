using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.App.WinForms.Components;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Resource.Components;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using CliWrap;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Newtonsoft.Json;
using Quartz;
using Semver;
using Serilog;
using SevenZipExtractor;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace Bakabase.InsideWorld.App.WinForms
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (Process.GetProcessesByName("Bakabase.InsideWorld").Length > 1)
                {
                    return;
                }

                ApplicationConfiguration.Initialize();

                AppService.SetupAppEnvironment("Bakabase.InsideWorld.App").ConfigureAwait(false).GetAwaiter()
                    .GetResult();

                Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

                // To customize application configuration such as set high DPI settings or default font,
                // see https://aka.ms/applicationconfiguration.
                // var mainWindow = new MainWindow();

//                 Task.Run(async () =>
//                 {
//                     try
//                     {
                        var hostBuilder = await InsideWorldHost.CreateBakabaseHostBuilder(args);

                        var guiHelper = new GuiAdapter(mainWindow);

                        hostBuilder = hostBuilder.ConfigureServices((context, collection) =>
                        {
                            collection.AddTransient<IGuiAdapter>(sp => guiHelper);
                        });
#if RELEASE
                        var port = NetworkUtils.FreeTcpPort();
                        hostBuilder = hostBuilder.ConfigureWebHost(t => t.UseUrls($"http://localhost:{port}"));
#endif
                        var host = hostBuilder.Build();

                        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                        lifetime.ApplicationStarted.Register(() => { mainWindow.OnHostStarted(host); });
                        lifetime.ApplicationStopped.Register(Application.Exit);

                        await host.RunAsync();
//                     }
//                     catch (Exception e)
//                     {
//                         await mainWindow.OnHostFailedToStart(e.BuildFullInformationText());
//                     }
//                 });


                var launchDialog = new LaunchDialog();

                Application.Run();
            }
            catch (Exception e)
            {
                Log.Logger.Error(e.BuildFullInformationText());
                Console.WriteLine(e.BuildFullInformationText());
            }
        }
    }
}