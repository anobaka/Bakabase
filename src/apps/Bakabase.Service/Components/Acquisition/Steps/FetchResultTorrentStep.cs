using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>A workflow decides whether torrent results should become files; the platform does not.</summary>
public sealed class FetchResultTorrentStep : IAcquisitionStep
{
    private readonly FetchTorrentStep _torrent = new();
    public string Kind => AcquisitionStepKinds.FetchResultTorrent;
    public string DisplayName => "Download returned torrent contents";
    public string Description => "Download torrent metadata returned by the preceding platform node. Already downloaded files continue unchanged.";
    public string DescriptionKey => "workflow.acquisition.fetchResultTorrent.description";
    public Type? ConfigType => typeof(FetchTorrentStep.Config);

    public Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct) => _torrent.ValidateConfigurationAsync(context, ct);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        item.Variables.TryGetValue(FetchExHentaiStep.ResultKindVariable, out var kind);
        if (kind == nameof(DownloadResultKind.LocalFiles))
            return new AcquisitionStepOutcome.Skip("The platform already downloaded the resource files.", item);
        if (kind == nameof(DownloadResultKind.TorrentMetadata))
        {
            var outcome = await _torrent.ExecuteAsync(ctx, item, ct);
            if (outcome is AcquisitionStepOutcome.Continue ready)
                foreach (var observer in ctx.ServiceProvider.GetServices<IAcquisitionContentsObserver>())
                    await observer.OnContentsReadyAsync(ctx, ready.Item, ready.Item.ExtractedDirectory!, ready.Item.Files, ct);
            return outcome;
        }
        return new AcquisitionStepOutcome.Fail("A preceding platform node must provide files or torrent metadata.");
    }
}
