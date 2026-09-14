using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Service.Components.Acquisition.Downloads;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>Requests platform work and carries its result back into the same acquisition run.</summary>
public sealed class FetchExHentaiStep : IAcquisitionStep
{
    public const string ResultKindVariable = "downloadResult.kind";
    public const string ResultIdVariable = "downloadResultId";
    public string Kind => AcquisitionStepKinds.FetchExHentai;
    public string DisplayName => "Obtain an ExHentai gallery";
    public string Description => "Ask the ExHentai downloader for this gallery and wait for its files or torrent metadata. A following torrent node downloads torrent contents.";
    public string DescriptionKey => "workflow.acquisition.fetchExHentai.description";
    public IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds => [AcquisitionLeadKind.PlatformHolding];
    public Type? ConfigType => null;

    public Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AcquisitionValidationIssue>>(context.LeadKind == null ||
            context.LeadKind == AcquisitionLeadKind.PlatformHolding && TrySource(context.LeadValue, out _)
                ? []
                : [new("exHentaiSourceInvalid", "This node requires an ExHentai gallery identity.",
                    "workflow.validation.acquisition.exHentaiSourceInvalid")]);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        if (!TrySource(item.LeadValue, out var sourceKey) || ctx.AcquisitionTaskId is not > 0 ||
            ctx.WorkflowRunId is not > 0)
            return new AcquisitionStepOutcome.Fail("This node requires an owned ExHentai acquisition run.");
        try
        {
            var state = await ctx.ServiceProvider.GetRequiredService<ExHentaiAcquisitionService>().StartAsync(
                ctx.AcquisitionTaskId.Value, ctx.WorkflowRunId.Value, item.ResourceId, sourceKey,
                item.Title, ctx.WorkingDirectory, ct);
            return await Receive(ctx, item, state, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new AcquisitionStepOutcome.Fail(ex.Message, ex); }
    }

    public async Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
    {
        if (ctx.AcquisitionTaskId is not > 0)
            return new AcquisitionStepOutcome.Fail("The platform download has no acquisition owner.");
        try
        {
            var state = await ctx.ServiceProvider.GetRequiredService<ExHentaiAcquisitionService>()
                .GetAsync(ctx.AcquisitionTaskId.Value, item.ResourceId, ct);
            return state == null
                ? new AcquisitionStepOutcome.Fail("The platform download disappeared.")
                : await Receive(ctx, item, state, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new AcquisitionStepOutcome.Fail(ex.Message, ex); }
    }

    private static async Task<AcquisitionStepOutcome> Receive(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, ExHentaiAcquisitionState state, CancellationToken ct)
    {
        if (state.Error != null) return new AcquisitionStepOutcome.Fail(state.Error);
        if (state.Result is not { } result)
        {
            TrySource(item.LeadValue, out var sourceKey);
            return new AcquisitionStepOutcome.Suspend(AcquisitionWaitReason.PlatformFetch,
                JsonSerializer.Serialize(new FetchFromPlatformStep.Prompt("ExHentai", sourceKey,
                    "Waiting for the platform download result.", DateTime.Now), JsonSerializerOptions.Web), item);
        }
        var variables = new Dictionary<string, string>(item.Variables)
        {
            [ResultKindVariable] = result.Kind.ToString(), [ResultIdVariable] = result.Id.ToString()
        };
        if (result.Kind == DownloadResultKind.TorrentMetadata)
        {
            await using var stream = File.OpenRead(result.Path);
            var bytes = await AcquisitionTorrentMetadataStore.ReadBoundedAsync(stream, ct);
            var reference = await ctx.ServiceProvider.GetRequiredService<IAcquisitionTorrentMetadataStore>()
                .SaveAsync(bytes, ct);
            return new AcquisitionStepOutcome.Continue(item with
            {
                Links = [new AcquisitionLink(reference)], SelectedLinkIndex = 0, Variables = variables,
                Files = [], ExtractedDirectory = null, TargetDirectory = null
            });
        }
        if (result.Kind != DownloadResultKind.LocalFiles)
            return new AcquisitionStepOutcome.Fail("The platform returned an unsupported result.");
        var files = JsonSerializer.Deserialize<string[]>(result.FilesJson) ?? [];
        var root = Path.GetFullPath(result.Path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(result.Path) || files.Length == 0 || files.Any(f =>
                !File.Exists(f) || !Path.GetFullPath(f).StartsWith(root, StringComparison.Ordinal)))
            return new AcquisitionStepOutcome.Fail("The platform result files are missing or outside their content directory.");
        var ready = item with
        {
            Files = files, ExtractedDirectory = result.Path, PreserveDirectoryStructure = true,
            TargetDirectory = null, Variables = variables, Links = [], SelectedLinkIndex = null
        };
        foreach (var observer in ctx.ServiceProvider.GetServices<IAcquisitionContentsObserver>())
            await observer.OnContentsReadyAsync(ctx, ready, result.Path, files, ct);
        return new AcquisitionStepOutcome.Continue(ready);
    }

    private static bool TrySource(string? value, out string sourceKey)
    {
        sourceKey = "";
        if (!FetchFromPlatformStep.TryReadLead(value ?? "", out var source, out var key) ||
            source != ResourceSource.ExHentai) return false;
        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId) || galleryId <= 0 ||
            parts[1].Any(c => !char.IsAsciiLetterOrDigit(c))) return false;
        sourceKey = $"{galleryId}/{parts[1]}";
        return true;
    }
}
