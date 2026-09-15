using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Microsoft.Extensions.DependencyInjection;

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
    public string Description => "Downloads HTTP(S) files with configurable parallel connections, retries, speed limits and a timeout. Resumes only when saved progress and the server's file version can be verified.";
    public string DescriptionKey => "workflow.activity.acquisition.fetchHttp.description";
    public IReadOnlyList<AcquisitionLeadKind> AcceptedLeadKinds => [AcquisitionLeadKind.DirectUrl];
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        public int TimeoutMinutes { get; init; } = 240;
        public int ParallelConnections { get; init; } = 4;
        public int MaxRetries { get; init; } = 3;
        public int SpeedLimitKiB { get; init; }
    }

    public Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct)
    {
        var issues = ValidateConfig(context.GetConfig<Config>() ?? new Config());
        if (context.IsExecution && context.LeadKind == AcquisitionLeadKind.DirectUrl &&
            (!Uri.TryCreate(context.LeadValue, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            issues.Add(new("httpSourceInvalid", "Enter an HTTP(S) URL for the file itself.", "workflow.validation.acquisition.httpSourceInvalid"));
        return Task.FromResult<IReadOnlyList<AcquisitionValidationIssue>>(issues);
    }

    private static List<AcquisitionValidationIssue> ValidateConfig(Config config)
    {
        var issues = new List<AcquisitionValidationIssue>();
        if (config.TimeoutMinutes is < 1 or > 43200)
            issues.Add(new("timeoutInvalid", "The download timeout must be between 1 and 43200 minutes.", "workflow.validation.acquisition.timeoutInvalid"));
        if (config.ParallelConnections is < 1 or > 16)
            issues.Add(new("httpConnectionsInvalid", "Use between 1 and 16 parallel connections.", "workflow.validation.acquisition.httpConnectionsInvalid"));
        if (config.MaxRetries is < 0 or > 10)
            issues.Add(new("httpRetriesInvalid", "The retry count must be between 0 and 10.", "workflow.validation.acquisition.httpRetriesInvalid"));
        if (config.SpeedLimitKiB is < 0 or > 1048576)
            issues.Add(new("httpSpeedLimitInvalid", "The speed limit must be between 0 and 1048576 KiB/s; zero means unlimited.", "workflow.validation.acquisition.httpSpeedLimitInvalid"));
        return issues;
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

        var config = ctx.GetConfig<Config>() ?? new Config();
        var issues = ValidateConfig(config);
        if (issues.Count > 0)
            return new AcquisitionStepOutcome.Fail(string.Join(" ", issues.Select(issue => issue.Message)));

        try
        {
            var path = await ctx.ServiceProvider.GetRequiredService<IHttpDownloader>().DownloadAsync(
                new HttpDownloadRequest(link.Url, ctx.WorkingDirectory)
                {
                    Timeout = TimeSpan.FromMinutes(config.TimeoutMinutes),
                    ParallelConnections = config.ParallelConnections,
                    MaxRetries = config.MaxRetries,
                    MaximumBytesPerSecond = config.SpeedLimitKiB * 1024L
                }, ctx.ReportProgress, ct);

            return new AcquisitionStepOutcome.Continue(item with
            {
                // Distinct: a re-run after a restart downloads to the same name and must not list
                // the same file twice.
                Files = item.Files.Append(path).Distinct().ToList()
            });
        }
        catch (TimeoutException)
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
