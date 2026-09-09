using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Downloads the chosen link straight into the run's working directory.
/// <para>
/// Only for links that are the file itself. Anything behind a login or a captcha — which is most
/// cloud drives — goes through the inbox instead, because automating those works right up until the
/// day it does not.
/// </para>
/// </summary>
public class FetchHttpStep : IAcquisitionStep
{
    public string Kind => AcquisitionStepKinds.FetchHttp;
    public string DisplayName => "Download the link";
    public Type? ConfigType => null;

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var link = item.SelectedLink ?? item.Links.FirstOrDefault();

        if (link == null)
        {
            return new AcquisitionStepOutcome.Fail("There is no link to download.");
        }

        if (!link.DriveKind.CanBeFetchedDirectly())
        {
            // Not a failure: a recipe may put this step in front of the inbox one so that the
            // easy case is automatic and the rest still works.
            return new AcquisitionStepOutcome.Skip(
                $"A {link.DriveKind} link cannot be downloaded without a person.", item);
        }

        Directory.CreateDirectory(ctx.WorkingDirectory);

        var factory = ctx.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var loggerFactory = ctx.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloader = new SingleFileHttpDownloader(
            factory.CreateClient(nameof(FetchHttpStep)),
            loggerFactory.CreateLogger<SingleFileHttpDownloader>());

        downloader.OnProgress += p => ctx.ReportProgress(p, "Downloading");

        try
        {
            var path = await downloader.DownloadToDirectory(link.Url, ctx.WorkingDirectory, ct);

            return new AcquisitionStepOutcome.Continue(item with
            {
                // Distinct: a re-run after a restart downloads to the same name and must not list
                // the same file twice.
                Files = item.Files.Append(path).Distinct().ToList()
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"The download failed: {ex.Message}", ex);
        }
    }
}
