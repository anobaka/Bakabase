using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Service.Components.FileProcessing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Opens the sharing page for the user and waits for the file to land in the inbox.
/// <para>
/// The one step that deliberately leaves work to a person. Cloud drives sit behind logins,
/// captchas and clients that change every few months; automating them works until it does not, and
/// then it fails silently in the middle of the night. Asking someone to click download, and
/// noticing when they have, is the arrangement that keeps working.
/// </para>
/// </summary>
public class WaitForInboxStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.WaitForInbox;
    public string DisplayName => "Wait for the file in the pending processing folder";
    public string Description => "Wait for files the user downloaded separately. Requires a server pending directory; the user confirms which files belong to the task.";
    public string DescriptionKey => "workflow.activity.acquisition.waitForInbox.description";
    public IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds =>
        [AcquisitionLeadKind.SharedPage, AcquisitionLeadKind.SharedDocument, AcquisitionLeadKind.DirectUrl,
            AcquisitionLeadKind.Magnet, AcquisitionLeadKind.Torrent];
    public Type? ConfigType => typeof(Config);

    public Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct) =>
        Task.FromResult(AcquisitionConfigurationValidation.Directory(
            context.Services.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.InboxDirectory,
            "acquisition.inbox.missing", "Choose a pending processing directory in the acquisition settings.",
            "workflow.validation.acquisition.inboxMissing"));

    public record Config
    {
        /// <summary>
        /// Open the sharing page in the browser when the run reaches this step. On by default: the
        /// user is being asked to fetch something, so putting them on the page is the point.
        /// </summary>
        public bool OpenLink { get; init; } = true;
    }

    /// <summary>Everything the interface needs to help the user fetch it by hand.</summary>
    public record Prompt(
        string? Url,
        string? AccessCode,
        string? ExpectedFileName,
        string? InboxDirectory,
        DateTime WaitingSince, string? ExtractionPlanJson = null);

    /// <summary>The answer: the files in the inbox that belong to this run.</summary>
    public record ClaimSignal(IReadOnlyList<string>? Files = null, string? Directory = null, bool AlreadyProcessed = false);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeliveryGates = new();
    public record DeliveryEntry(string Source, string Target, string Hash);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.InboxDirectory))
        {
            return new AcquisitionStepOutcome.Fail(
                "No pending processing folder is set. Point it at wherever your browser saves downloads.");
        }

        var link = item.SelectedLink ?? item.Links.FirstOrDefault();

        if (ctx.GetConfig<Config>() is not {OpenLink: false} && link != null)
        {
            OpenInBrowser(ctx, link.Url);
        }

        return new AcquisitionStepOutcome.Suspend(
            AcquisitionWaitReason.WaitingForFile,
            JsonSerializer.Serialize(new Prompt(
                link?.Url,
                link?.AccessCode,
                item.Variables.GetValueOrDefault("expectedFileName"),
                options.InboxDirectory,
                DateTime.Now, item.ExtractionPlanJson ?? link?.ExtractionPlanJson), Json),
            item);
    }

    public async Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
    {
        ClaimSignal? claim = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { claim = JsonSerializer.Deserialize<ClaimSignal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return new AcquisitionStepOutcome.Fail($"The claim was not readable: {ex.Message}");
            }
        }

        if (claim == null || (claim.Files is not {Count: > 0} && string.IsNullOrWhiteSpace(claim.Directory)))
            return new AcquisitionStepOutcome.Fail("Select files or a directory to deliver.");
        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.InboxDirectory))
            return new AcquisitionStepOutcome.Fail("Choose a pending processing directory first.");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Files = claim.Files?.Distinct(FileProcessingFiles.PathComparer).OrderBy(x => x, FileProcessingFiles.PathComparer).ToArray(),
            claim.Directory
        }, Json))));
        var journalDirectory = ctx.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".delivery";
        var journalPath = Path.Combine(journalDirectory, identity + ".json");
        var gate = DeliveryGates.GetOrAdd(journalPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var storage = ctx.ServiceProvider.GetRequiredService<IUserStoragePolicy>();
            var inbox = Path.GetFullPath(options.InboxDirectory);
            storage.EnsurePathAllowed(inbox);
            var root = claim.Directory == null ? inbox : FileProcessingFiles.Within(claim.Directory, inbox);
            storage.EnsurePathAllowed(root);
            List<DeliveryEntry> entries;
            if (File.Exists(journalPath))
                entries = JsonSerializer.Deserialize<List<DeliveryEntry>>(await File.ReadAllTextAsync(journalPath, ct), Json)!;
            else
            {
                var selected = claim.Directory == null ? new List<string>() : FileProcessingFiles.Enumerate(root);
                if (claim.Files != null) selected.AddRange(claim.Files.Select(f => FileProcessingFiles.Within(f, root)));
                var files = (claim.AlreadyProcessed ? selected.Distinct(FileProcessingFiles.PathComparer).ToList() : FileProcessingFiles.ExpandVolumes(selected))
                    .Select(f => FileProcessingFiles.Within(f, root)).ToList();
                if (files.Count == 0) return new AcquisitionStepOutcome.Fail("No files were selected for this acquisition.");
                if (!claim.AlreadyProcessed) FileProcessingFiles.ValidateVolumes(files);
                entries = [];
                foreach (var file in files)
                {
                    storage.EnsurePathAllowed(file);
                    if (!AcquisitionInboxService.IsStable(file))
                        return new AcquisitionStepOutcome.Fail($"{Path.GetFileName(file)} is missing or still downloading.");
                    var target = FileProcessingFiles.Within(Path.Combine(ctx.WorkingDirectory,
                        Path.GetRelativePath(root, file)), ctx.WorkingDirectory);
                    if (File.Exists(target) || Directory.Exists(target))
                        return new AcquisitionStepOutcome.Fail($"{Path.GetFileName(target)} already exists in this task. No files were moved.");
                    entries.Add(new DeliveryEntry(file, target, await Fingerprint(file, ct)));
                }
                Directory.CreateDirectory(journalDirectory);
                await File.WriteAllTextAsync(journalPath + ".tmp", JsonSerializer.Serialize(entries, Json), ct);
                File.Move(journalPath + ".tmp", journalPath);
            }
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                storage.EnsurePathAllowed(entry.Source);
                FileProcessingFiles.Within(entry.Source, inbox);
                FileProcessingFiles.Within(entry.Target, ctx.WorkingDirectory);
                if (File.Exists(entry.Target))
                {
                    if (await Fingerprint(entry.Target, ct) != entry.Hash)
                        throw new InvalidOperationException("A delivered file was replaced. The claim cannot overwrite it.");
                    // A copied file remaining after a crash is removed only after content equality,
                    // never merely because two unrelated files happen to have the same size.
                    if (File.Exists(entry.Source))
                    {
                        if (await Fingerprint(entry.Source, ct) != entry.Hash)
                            throw new InvalidOperationException("The inbox file changed after it was selected.");
                        File.Delete(entry.Source);
                    }
                    continue;
                }
                if (!File.Exists(entry.Source) || await Fingerprint(entry.Source, ct) != entry.Hash)
                    throw new InvalidOperationException("A selected file changed or disappeared before delivery.");
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Target)!);
                File.Move(entry.Source, entry.Target);
            }
            await ctx.ReportProgress(100, BTaskText.Localize(
                ctx.ServiceProvider.GetRequiredService<IBakabaseLocalizer>(),
                "BTask_Process_FilesDelivered", entries.Count));
            return new AcquisitionStepOutcome.Continue(item with
            {
                Files = item.Files.Concat(entries.Select(e => e.Target)).Distinct(FileProcessingFiles.PathComparer).ToList(),
                AlreadyProcessed = claim.AlreadyProcessed,
                ExtractedDirectory = claim.AlreadyProcessed ? ctx.WorkingDirectory : item.ExtractedDirectory,
                PreserveDirectoryStructure = claim.Directory != null || item.PreserveDirectoryStructure
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AcquisitionStepOutcome.Fail($"Could not deliver the selected files: {ex.Message}", ex);
        }
        finally { gate.Release(); }
    }

    private static async Task<string> Fingerprint(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static void OpenInBrowser(AcquisitionStepContext ctx, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) {UseShellExecute = true});
        }
        catch (Exception ex)
        {
            // Headless, or no browser. The prompt still carries the link, so the run is not stuck.
            ctx.Logger.LogInformation(ex, "[Acquisition] Could not open {Url}; the prompt still has it", url);
        }
    }

}
