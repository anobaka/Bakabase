using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
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
/// Extracts whatever was downloaded, trying the passwords it knows about.
/// <para>
/// The password is the part that goes wrong. It might be in the post, in the file name, or nowhere
/// at all — and the shape of that problem is why this step exists rather than a call to the
/// extractor: it has to be able to stop and ask, and to carry on from the answer.
/// </para>
/// </summary>
public class UnpackStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.Unpack;
    public string DisplayName => "Unpack";
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        /// <summary>Send the archive to the recycle bin once its contents are out.</summary>
        public bool DeleteArchive { get; init; } = true;

        /// <summary>
        /// How many times to unwrap an archive that contained another archive. Two by default:
        /// a zip inside a rar is common, a third layer almost never is.
        /// </summary>
        public int MaxDepth { get; init; } = 2;

        /// <summary>
        /// Try passwords used recently when the ones in the content and the file name fail. Off for
        /// anyone who would rather be asked than have Bakabase work through their password list.
        /// </summary>
        public bool? TryRecentPasswords { get; init; }
    }

    /// <summary>What the interface asks for when nothing opened the archive.</summary>
    public record Prompt(string ArchiveName, IReadOnlyList<string> Tried);

    /// <summary>The answer: a password to try.</summary>
    public record PasswordSignal(string Password);

    public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        CancellationToken ct) => UnpackAsync(ctx, item, null, ct);

    public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        AcquisitionResumeSignal signal, CancellationToken ct)
    {
        PasswordSignal? answer = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { answer = JsonSerializer.Deserialize<PasswordSignal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return Task.FromResult<AcquisitionStepOutcome>(
                    new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}"));
            }
        }

        return UnpackAsync(ctx, item, answer?.Password, ct);
    }

    private async Task<AcquisitionStepOutcome> UnpackAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, string? suppliedPassword, CancellationToken ct)
    {
        var config = ctx.GetConfig<Config>() ?? new Config();
        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;
        var extraction = ctx.ServiceProvider.GetRequiredService<IArchiveExtractionService>();

        var directory = item.ExtractedDirectory ?? ctx.WorkingDirectory;

        if (!Directory.Exists(directory))
        {
            return new AcquisitionStepOutcome.Skip("There is nothing here to unpack.", item);
        }

        var current = item;
        var unpackedAnything = false;
        // An archive the user asked to keep is still sitting there on the next pass. Without this
        // it would be unpacked again, over its own output, and 7-Zip would rightly refuse.
        var alreadyUnpacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var depth = 0; depth < Math.Max(1, config.MaxDepth); depth++)
        {
            ct.ThrowIfCancellationRequested();

            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            var groups = CompressedFileHelper.DetectCompressedFileGroups(files, true)
                .Where(g => !alreadyUnpacked.Contains(g.Files[0]))
                .ToList();

            if (groups.Count == 0) break;

            foreach (var group in groups)
            {
                alreadyUnpacked.Add(group.Files[0]);

                var outcome = await UnpackOneAsync(ctx, extraction, options, config, group,
                    current, suppliedPassword, ct);

                // A suspension or a failure ends the step; only the password we were just given is
                // spent, so the next attempt starts from the candidate list again.
                if (outcome is not AcquisitionStepOutcome.Continue continued) return outcome;

                current = continued.Item;
                unpackedAnything = true;
                suppliedPassword = null;
            }
        }

        if (!unpackedAnything)
        {
            // Not a failure: plenty of downloads are just a folder of files.
            return new AcquisitionStepOutcome.Skip("Nothing here is an archive.", current);
        }

        return new AcquisitionStepOutcome.Continue(current with {ExtractedDirectory = directory});
    }

    private async Task<AcquisitionStepOutcome> UnpackOneAsync(AcquisitionStepContext ctx,
        IArchiveExtractionService extraction, AcquisitionOptions options, Config config,
        CompressedFileGroup group, AcquisitionWorkItem item, string? suppliedPassword, CancellationToken ct)
    {
        var entry = group.Files[0];
        var directory = Path.GetDirectoryName(entry)!;
        var candidates = await BuildCandidatesAsync(ctx, options, config, item, entry, suppliedPassword);

        await ctx.ReportProgress(0, $"Opening {Path.GetFileName(entry)}");

        var probe = await extraction.ProbePasswordAsync(entry, candidates, null, ct);

        if (!probe.Succeeded)
        {
            // Everything known has been tried. Asking is the only thing left, and it is a far
            // better outcome than a failed run the user has to go and read a log about.
            return new AcquisitionStepOutcome.Suspend(
                AcquisitionWaitReason.PasswordUnknown,
                JsonSerializer.Serialize(new Prompt(Path.GetFileName(entry),
                    candidates.Where(c => c != null).Cast<string>().ToList()), Json),
                item);
        }

        var result = await extraction.ExtractAsync(
            new ArchiveExtractionRequest(
                group.Files,
                directory,
                probe.Password,
                DecompressToNewFolder: true,
                OverwriteExistingFiles: false,
                DeleteAfterDecompression: config.DeleteArchive,
                // Lifted up a level: an archive holding one folder would otherwise nest that folder
                // inside a folder named after the archive, which is never what anyone wanted.
                MoveToParent: true),
            percentage => ctx.ReportProgress(percentage, $"Unpacking {Path.GetFileName(entry)}")
                .GetAwaiter().GetResult(),
            ct);

        if (!result.Succeeded)
        {
            return new AcquisitionStepOutcome.Fail(
                $"Could not unpack {Path.GetFileName(entry)}: {Shorten(result.Message)}");
        }

        if (probe.Password is { } used)
        {
            // Remembered so the next archive from the same place opens without asking.
            try
            {
                await ctx.ServiceProvider.GetRequiredService<PasswordService>().AddUsedTimes(used);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogWarning(ex, "[Acquisition] Could not record the password that worked");
            }
        }

        return new AcquisitionStepOutcome.Continue(item with
        {
            Variables = With(item.Variables, "archivePassword", probe.Password),
            // The archives are gone (or on their way to the bin); what is left is what came out.
            Files = item.Files.Where(f => !group.Files.Contains(f)).ToList()
        });
    }

    /// <summary>
    /// Most likely first, so the common case costs one call to 7-Zip: no password at all, then what
    /// the post said, then what the file name says, then — only if asked for — the passwords that
    /// have worked before.
    /// </summary>
    private static async Task<List<string?>> BuildCandidatesAsync(AcquisitionStepContext ctx,
        AcquisitionOptions options, Config config, AcquisitionWorkItem item, string entry,
        string? suppliedPassword)
    {
        var candidates = new List<string?>();

        void Add(string? p)
        {
            if (!string.IsNullOrEmpty(p) && !candidates.Contains(p)) candidates.Add(p);
        }

        // A password the user has just typed goes first — they are standing there waiting for it.
        Add(suppliedPassword);
        candidates.Add(null);

        Add(item.SelectedLink?.ArchivePassword);
        foreach (var link in item.Links) Add(link.ArchivePassword);
        Add(item.Variables.GetValueOrDefault("archivePassword"));

        foreach (var p in entry.GetPasswordsFromPath()) Add(p);

        if (config.TryRecentPasswords ?? options.TryRecentPasswords)
        {
            try
            {
                var recent = await ctx.ServiceProvider.GetRequiredService<PasswordService>()
                    .Search(new PasswordSearchRequestModel
                    {
                        PageIndex = 1,
                        PageSize = Math.Max(1, options.RecentPasswordCandidateCount),
                        Order = PasswordSearchOrder.Frequency,
                    });

                foreach (var p in recent.Data ?? []) Add(p.Text);
            }
            catch (Exception ex)
            {
                ctx.Logger.LogWarning(ex, "[Acquisition] Could not read the recent passwords");
            }
        }

        return candidates;
    }

    private static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> variables,
        string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return variables;

        return new Dictionary<string, string>(variables) {[key] = value};
    }

    private static string Shorten(string message) =>
        message.Length <= 300 ? message.Trim() : message.Trim()[..300] + "…";
}
