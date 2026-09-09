using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public string DisplayName => "Wait for the file in the inbox";
    public Type? ConfigType => typeof(Config);

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
        DateTime WaitingSince);

    /// <summary>The answer: the files in the inbox that belong to this run.</summary>
    public record ClaimSignal(IReadOnlyList<string> Files);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.InboxDirectory))
        {
            return new AcquisitionStepOutcome.Fail(
                "No inbox directory is set. Point it at wherever your browser saves downloads.");
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
                DateTime.Now), Json),
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

        if (claim?.Files is not {Count: > 0} claimed)
        {
            return new AcquisitionStepOutcome.Fail("No files were claimed for this acquisition.");
        }

        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        var inbox = options.InboxDirectory;

        Directory.CreateDirectory(ctx.WorkingDirectory);

        var moved = new List<string>();

        foreach (var source in claimed)
        {
            if (!File.Exists(source))
            {
                return new AcquisitionStepOutcome.Fail($"\"{Path.GetFileName(source)}\" is no longer there.");
            }

            // Everything claimed has to come from the inbox. The claim arrives over an API, and a
            // path outside it would let a request move any file on the machine.
            if (!string.IsNullOrWhiteSpace(inbox) && !IsInside(source, inbox))
            {
                return new AcquisitionStepOutcome.Fail(
                    $"\"{Path.GetFileName(source)}\" is not in the inbox.");
            }

            var target = Path.Combine(ctx.WorkingDirectory, Path.GetFileName(source));

            try
            {
                if (File.Exists(target))
                {
                    // A re-run after a restart finds its own earlier move already done.
                    if (new FileInfo(target).Length == new FileInfo(source).Length)
                    {
                        File.Delete(source);
                        moved.Add(target);

                        continue;
                    }

                    target = Deduplicate(target);
                }

                File.Move(source, target);
                moved.Add(target);
            }
            catch (IOException ex)
            {
                return new AcquisitionStepOutcome.Fail(
                    $"Could not move \"{Path.GetFileName(source)}\" out of the inbox: {ex.Message}", ex);
            }
        }

        await ctx.ReportProgress(100, $"{moved.Count} files claimed");

        return new AcquisitionStepOutcome.Continue(item with
        {
            Files = item.Files.Concat(moved).Distinct().ToList()
        });
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

    private static bool IsInside(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Deduplicate(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2;; i++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");

            if (!File.Exists(candidate)) return candidate;
        }
    }
}
