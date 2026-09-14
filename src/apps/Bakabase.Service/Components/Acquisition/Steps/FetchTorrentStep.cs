using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Service.Components.Acquisition.Downloads;
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
            byte[] metadata;
            if (reference.StartsWith("bakabase-torrent:", StringComparison.Ordinal))
                metadata = await ctx.ServiceProvider.GetRequiredService<IAcquisitionTorrentMetadataStore>()
                    .ReadAsync(reference, deadline.Token);
            else
            {
                await ctx.ReportProgress(0, "Reading the torrent file");
                using var http = ctx.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(FetchTorrentStep));
                using var response = await http.GetAsync(reference, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > AcquisitionTorrentMetadataStore.MaxMetadataBytes)
                    return new AcquisitionStepOutcome.Fail("The torrent metadata exceeds 4 MiB.");
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
                metadata = await AcquisitionTorrentMetadataStore.ReadBoundedAsync(input, deadline.Token);
            }
            var downloaded = await ctx.ServiceProvider.GetRequiredService<IAcquisitionTorrentDownloader>()
                .DownloadTorrentAsync(metadata, ctx.WorkingDirectory, TimeSpan.FromMinutes(config.TimeoutMinutes),
                    ctx.ReportProgress, deadline.Token);
            return new AcquisitionStepOutcome.Continue(item with
            {
                Files = item.Files.Concat(downloaded.Files).Distinct().ToList(),
                ExtractedDirectory = downloaded.Directory,
                PreserveDirectoryStructure = true
            });
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
