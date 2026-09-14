using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
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
    public string Description => "Downloads an HTTP(S) file on the server with progress, cancellation and a timeout; restarts when an existing file cannot be verified.";
    public string DescriptionKey => "workflow.activity.acquisition.fetchHttp.description";
    public IReadOnlyList<AcquisitionLeadKind> AcceptedLeadKinds => [AcquisitionLeadKind.DirectUrl];
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        public int TimeoutMinutes { get; init; } = 240;
    }

    public Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct)
    {
        var issues = new List<AcquisitionValidationIssue>();
        if ((context.GetConfig<Config>() ?? new Config()).TimeoutMinutes is < 1 or > 43200)
            issues.Add(new("timeoutInvalid", "The download timeout must be between 1 and 43200 minutes.", "workflow.validation.acquisition.timeoutInvalid"));
        if (context.IsExecution && context.LeadKind == AcquisitionLeadKind.DirectUrl &&
            (!Uri.TryCreate(context.LeadValue, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            issues.Add(new("httpSourceInvalid", "Enter an HTTP(S) URL for the file itself.", "workflow.validation.acquisition.httpSourceInvalid"));
        return Task.FromResult<IReadOnlyList<AcquisitionValidationIssue>>(issues);
    }

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

        var config = ctx.GetConfig<Config>() ?? new Config();
        if (config.TimeoutMinutes is < 1 or > 43200)
            return new AcquisitionStepOutcome.Fail("The download timeout must be between 1 and 43200 minutes.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));

        try
        {
            var path = await downloader.DownloadToDirectory(link.Url, ctx.WorkingDirectory, deadline.Token);

            return new AcquisitionStepOutcome.Continue(item with
            {
                // Distinct: a re-run after a restart downloads to the same name and must not list
                // the same file twice.
                Files = item.Files.Append(path).Distinct().ToList()
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return new AcquisitionStepOutcome.Fail($"The HTTP download did not finish within {config.TimeoutMinutes} minutes.");
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
