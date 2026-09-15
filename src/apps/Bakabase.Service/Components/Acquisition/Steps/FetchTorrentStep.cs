using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Steps;

public class FetchTorrentStep : IAcquisitionStep
{
    public string Kind => AcquisitionStepKinds.FetchTorrent;
    public string DisplayName => "Download the torrent";
    public string Description => "Reads an uploaded .torrent or its HTTP(S) URL and downloads the complete file set on the server. Stops when verified.";
    public string DescriptionKey => "workflow.activity.acquisition.fetchTorrent.description";
    public IReadOnlyList<AcquisitionLeadKind> AcceptedLeadKinds => [AcquisitionLeadKind.Torrent];
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
        if (context.IsExecution && context.LeadKind == AcquisitionLeadKind.Torrent && !IsSupportedReference(context.LeadValue))
            issues.Add(new("torrentSourceInvalid", "Upload a .torrent file or enter its HTTP(S) URL; local filesystem paths are not accepted.", "workflow.validation.acquisition.torrentSourceInvalid"));
        return Task.FromResult<IReadOnlyList<AcquisitionValidationIssue>>(issues);
    }

    private static bool IsSupportedReference(string? value) =>
        AcquisitionTorrentMetadataStore.IsManagedReference(value) ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        // Upstream parsing/selection may produce a torrent URL while the original lead is a
        // sharing page. The selected input takes precedence over that original source kind.
        var reference = item.SelectedLink?.Url ?? item.Links.FirstOrDefault()?.Url ??
            (item.LeadKind == AcquisitionLeadKind.Torrent ? item.LeadValue : null);
        if (string.IsNullOrWhiteSpace(reference) || !IsSupportedReference(reference))
            return new AcquisitionStepOutcome.Fail("Upload a .torrent file or enter its HTTP(S) URL.");
        var config = ctx.GetConfig<Config>() ?? new Config();
        if (config.TimeoutMinutes is < 1 or > 43200)
            return new AcquisitionStepOutcome.Fail("The download timeout must be between 1 and 43200 minutes.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes));
        try
        {
            var downloader = ctx.ServiceProvider.GetRequiredService<ITorrentDownloader>();
            TorrentDownloadResult downloaded;
            if (AcquisitionTorrentMetadataStore.IsManagedReference(reference))
            {
                var metadata = await ctx.ServiceProvider.GetRequiredService<IAcquisitionTorrentMetadataStore>()
                    .ReadAsync(reference, deadline.Token);
                downloaded = await downloader.DownloadTorrentAsync(metadata, ctx.WorkingDirectory,
                    TimeSpan.FromMinutes(config.TimeoutMinutes), ctx.ReportProgress, deadline.Token);
            }
            else
            {
                downloaded = await downloader.DownloadTorrentUrlAsync(reference, ctx.WorkingDirectory,
                    TimeSpan.FromMinutes(config.TimeoutMinutes), ctx.ReportProgress, deadline.Token);
            }
            return new AcquisitionStepOutcome.Continue(item with
            {
                Files = item.Files.Concat(downloaded.Files).Distinct().ToList(),
                ExtractedDirectory = downloaded.Directory,
                PreserveDirectoryStructure = true
            });
        }
        catch (TimeoutException)
        {
            return new AcquisitionStepOutcome.Fail($"The torrent download did not finish within {config.TimeoutMinutes} minutes.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return new AcquisitionStepOutcome.Fail($"The torrent download did not finish within {config.TimeoutMinutes} minutes.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"The torrent download failed: {ex.Message}", ex);
        }
    }
}
