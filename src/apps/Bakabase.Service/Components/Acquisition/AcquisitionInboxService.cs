using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Steps;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>One file in the inbox, with how well it matches each task that is waiting.</summary>
public record InboxCandidate(
    string Path,
    string FileName,
    long Length,
    DateTime CreatedAt,
    bool IsStable,
    IReadOnlyList<InboxCandidateScore> Scores);

public record InboxCandidateScore(int AcquisitionTaskId, string? ResourceName, int Score);

/// <summary>
/// Everything about the inbox that is not the watching itself: what is in it, what each waiting
/// task makes of what is in it, and putting a file back when a claim was wrong.
/// </summary>
public class AcquisitionInboxService(
    IAcquisitionService acquisitions,
    IWorkflowRunResumer resumer,
    IBOptions<AcquisitionOptions> options,
    Bakabase.Infrastructures.Components.App.AppService appService,
    ILogger<AcquisitionInboxService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Extensions browsers and download clients use while a file is still arriving. A file wearing
    /// one is not a file yet.
    /// </summary>
    private static readonly HashSet<string> PartialExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".tmp", ".!qb", ".td", ".aria2", ".opdownload"
    };

    public string? InboxDirectory => options.Value.InboxDirectory;

    public async Task<List<InboxCandidate>> ListAsync(CancellationToken ct = default)
    {
        var inbox = InboxDirectory;

        if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox)) return [];

        var waiting = await WaitingAsync(ct);

        // One level only: a download client's own subfolders are its business, and descending into
        // them would claim files out of somebody else's half-finished job.
        return new DirectoryInfo(inbox).EnumerateFiles()
            .Select(f =>
            {
                var file = new InboxFile(f.Name, f.CreationTime);

                return new InboxCandidate(f.FullName, f.Name, f.Length, f.CreationTime,
                    IsStable(f.FullName),
                    waiting
                        .Select(w => new InboxCandidateScore(w.Task.Id, w.Task.ResourceName,
                            InboxClaimScorer.Score(file, w.Expectation)))
                        .OrderByDescending(s => s.Score)
                        .ToList());
            })
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Offers one newly-settled file to the tasks that are waiting. Claims it when exactly one of
    /// them is a clear match; when two are close, says so on the run rather than guessing.
    /// </summary>
    public async Task OfferAsync(string filePath, CancellationToken ct = default)
    {
        if (!IsStable(filePath) || !IsCompleteVolumeSet(filePath)) return;

        var waiting = await WaitingAsync(ct);

        if (waiting.Count == 0) return;

        var info = new FileInfo(filePath);
        var file = new InboxFile(info.Name, info.CreationTime);
        var scored = waiting
            .Select(w => (w.Task, Score: InboxClaimScorer.Score(file, w.Expectation)))
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored[0];

        if (best.Score < InboxClaimScorer.ConfidentThreshold)
        {
            logger.LogDebug("[Inbox] Nothing is obviously waiting for {File}", info.Name);

            return;
        }

        var ambiguous = scored.Count > 1 &&
                        best.Score - scored[1].Score < InboxClaimScorer.TieMargin;

        if (ambiguous)
        {
            // Two things that look equally like the answer mean the guess is not worth making.
            // Every close contender is told, so whichever one the user opens explains itself.
            foreach (var (task, _) in scored.Where(s =>
                         best.Score - s.Score < InboxClaimScorer.TieMargin))
            {
                await MarkAmbiguousAsync(task, info, ct);
            }

            return;
        }

        await ClaimAsync(best.Task.Id, [filePath], ct);
        logger.LogInformation("[Inbox] {File} claimed for acquisition {TaskId} (score {Score})",
            info.Name, best.Task.Id, best.Score);
    }

    /// <summary>Hands the named files to a waiting task, which is what resuming its run means here.</summary>
    public Task ClaimAsync(int taskId, IReadOnlyList<string> files, CancellationToken ct = default) =>
        acquisitions.ResumeAsync(taskId, JsonSerializer.Serialize(
            new AcquisitionResumeSignal(AcquisitionWaitReason.WaitingForFile,
                JsonSerializer.Serialize(new WaitForInboxStep.ClaimSignal(files), Json)), Json), ct);

    /// <summary>
    /// Undoes a claim: the files go back to the inbox and the task stops, so another task — or the
    /// same one on a second try — can have them. Used when the watcher guessed wrong.
    /// </summary>
    public async Task<int> UnclaimAsync(int taskId, CancellationToken ct = default)
    {
        var inbox = InboxDirectory;

        if (string.IsNullOrWhiteSpace(inbox))
        {
            throw new InvalidOperationException("No inbox directory is set, so there is nowhere to put them back.");
        }

        var task = await acquisitions.GetAsync(taskId, ct)
                   ?? throw new InvalidOperationException($"Acquisition #{taskId} does not exist.");

        Directory.CreateDirectory(inbox);

        var workingDirectory = WorkingDirectoryOf(task);
        var moved = 0;

        if (Directory.Exists(workingDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(workingDirectory))
            {
                var target = Path.Combine(inbox, Path.GetFileName(path));

                if (File.Exists(target)) continue;

                File.Move(path, target);
                moved++;
            }
        }

        await acquisitions.CancelAsync(taskId, ct);

        return moved;
    }

    private async Task MarkAmbiguousAsync(AcquisitionTask task, FileInfo file, CancellationToken ct)
    {
        if (task.WorkflowRunId is not { } runId) return;

        try
        {
            await resumer.UpdateWaitAsync(runId, nameof(AcquisitionWaitReason.AmbiguousInboxFile),
                JsonSerializer.Serialize(new WaitForInboxStep.Prompt(null, null, file.Name,
                    InboxDirectory, task.WaitingSince ?? DateTime.Now), Json), ct);
        }
        catch (InvalidOperationException)
        {
            // It stopped waiting between the scan and now. Nothing to say to it.
        }
    }

    private async Task<List<(AcquisitionTask Task, InboxExpectation Expectation)>> WaitingAsync(CancellationToken ct)
    {
        var tasks = await acquisitions.SearchAsync(AcquisitionStatus.Waiting, ct: ct);

        return tasks
            .Where(t => t.WaitReason is AcquisitionWaitReason.WaitingForFile
                or AcquisitionWaitReason.AmbiguousInboxFile)
            .Select(t => (t, ExpectationOf(t)))
            .ToList();
    }

    private static InboxExpectation ExpectationOf(AcquisitionTask task)
    {
        WaitForInboxStep.Prompt? prompt = null;

        if (!string.IsNullOrWhiteSpace(task.WaitPromptJson))
        {
            try { prompt = JsonSerializer.Deserialize<WaitForInboxStep.Prompt>(task.WaitPromptJson, Json); }
            catch (JsonException) { /* an unreadable prompt still leaves the title to match on */ }
        }

        return new InboxExpectation(task.ResourceName, prompt?.ExpectedFileName, prompt?.AccessCode,
            task.WaitingSince ?? task.CreatedAt);
    }

    /// <summary>Same layout the service uses when it starts a run; the two must not drift.</summary>
    private string WorkingDirectoryOf(AcquisitionTask task) =>
        Path.Combine(appService.AppDataDirectory, "acquisition", task.Id.ToString());

    /// <summary>
    /// Whether a file has finished arriving: not wearing a download client's temporary extension,
    /// and openable exclusively. Size stability is the watcher's job — it sees the file twice.
    /// </summary>
    public static bool IsStable(string path)
    {
        if (PartialExtensions.Contains(Path.GetExtension(path))) return false;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);

            return true;
        }
        catch (IOException)
        {
            // Still being written to.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether every part of a multi-part archive is present. Claiming <c>.part1.rar</c> the moment
    /// it settles would take it away while <c>.part2.rar</c> is still downloading, and the unpack
    /// step would then fail on a set it can never complete.
    /// </summary>
    public static bool IsCompleteVolumeSet(string path)
    {
        var name = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(directory)) return true;

        var match = System.Text.RegularExpressions.Regex.Match(name,
            @"^(?<stem>.+?)\.(?:part(?<num>\d+)\.rar|r(?<r>\d{2})|z(?<z>\d{2}))$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            // Also the numbered-suffix shape: name.7z.001
            match = System.Text.RegularExpressions.Regex.Match(name,
                @"^(?<stem>.+?\.(?:7z|zip|rar|tar))\.(?<num>\d{3})$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return true;
        }

        var stem = match.Groups["stem"].Value;
        var siblings = Directory.EnumerateFiles(directory, stem + ".*")
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();

        // Every volume of a set has to be settled, not just present: one still downloading means
        // the set is not ready however many files are sitting there.
        return siblings.All(n => IsStable(Path.Combine(directory, n)));
    }
}
