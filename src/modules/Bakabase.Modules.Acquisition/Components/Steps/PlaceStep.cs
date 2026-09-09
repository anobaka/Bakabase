using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Components.Steps;

/// <summary>What to do when there is already a folder of that name in the library.</summary>
public enum PlacementConflictPolicy
{
    /// <summary>Put it beside the existing one under a numbered name.</summary>
    Rename = 1,

    /// <summary>Move the new files into the existing folder, keeping whatever is already there.</summary>
    Merge = 2,

    /// <summary>Stop and let the user decide. The safe answer, and the default.</summary>
    Ask = 3
}

/// <summary>
/// Moves what was obtained into the library, under the name the recipe settled on.
/// <para>
/// The name is the item's working text, so any of the existing text activities can be dropped in
/// front of this step to clean it up — that is the whole reason a work item is an
/// <c>ITextWorkpiece</c>. This step only fills it in from the template when nothing else has.
/// </para>
/// </summary>
public class PlaceStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.Place;
    public string DisplayName => "File it away";
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        /// <summary>Where to file it. Absent uses the library root from the acquisition settings.</summary>
        public string? LibraryRootDirectory { get; init; }

        /// <summary>Overrides the directory template from the settings for this recipe.</summary>
        public string? DirectoryTemplate { get; init; }

        public PlacementConflictPolicy OnConflict { get; init; } = PlacementConflictPolicy.Ask;
    }

    /// <summary>What the interface shows when the destination is taken.</summary>
    public record Prompt(string TargetDirectory, int ExistingEntryCount);

    /// <summary>The answer: what to do about it.</summary>
    public record ConflictSignal(PlacementConflictPolicy Policy, string? RenameTo);

    public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        CancellationToken ct) => PlaceAsync(ctx, item, null, ct);

    public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        AcquisitionResumeSignal signal, CancellationToken ct)
    {
        ConflictSignal? answer = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { answer = JsonSerializer.Deserialize<ConflictSignal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return Task.FromResult<AcquisitionStepOutcome>(
                    new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}"));
            }
        }

        if (answer?.RenameTo is {Length: > 0} renamed)
        {
            item = item with {WorkingName = renamed};
        }

        return PlaceAsync(ctx, item, answer?.Policy, ct);
    }

    private async Task<AcquisitionStepOutcome> PlaceAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, PlacementConflictPolicy? policyOverride, CancellationToken ct)
    {
        var config = ctx.GetConfig<Config>() ?? new Config();
        var options = ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value;

        var libraryRoot = config.LibraryRootDirectory ?? options.LibraryRootDirectory;

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return new AcquisitionStepOutcome.Fail(
                "No library folder is set, so there is nowhere to put this.");
        }

        var source = ResolveSource(item, ctx.WorkingDirectory);

        if (source == null)
        {
            return new AcquisitionStepOutcome.Fail("There is nothing here to file away.");
        }

        // Only when nothing else named it: a text activity earlier in the recipe has the last word.
        var name = string.IsNullOrWhiteSpace(item.WorkingName)
            ? AcquisitionDirectoryNamer.Render(config.DirectoryTemplate ?? options.DirectoryTemplate, item)
            : AcquisitionDirectoryNamer.Render(item.WorkingName, item);

        var target = Path.Combine(libraryRoot, name);

        if (item.TargetDirectory is {Length: > 0} already && Directory.Exists(already) &&
            !Directory.Exists(source))
        {
            // Already done, and the source is gone because this step moved it. A re-run at the
            // cursor after a restart must not fail on its own success.
            return new AcquisitionStepOutcome.Continue(item);
        }

        var policy = policyOverride ?? config.OnConflict;

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            switch (policy)
            {
                case PlacementConflictPolicy.Rename:
                    target = Deduplicate(target);

                    break;

                case PlacementConflictPolicy.Merge:
                    break;

                default:
                    return new AcquisitionStepOutcome.Suspend(
                        AcquisitionWaitReason.TargetExists,
                        JsonSerializer.Serialize(new Prompt(target,
                            Directory.EnumerateFileSystemEntries(target).Count()), Json),
                        item with {WorkingName = name});
            }
        }

        try
        {
            Directory.CreateDirectory(libraryRoot);
            MoveInto(Collapse(source), target);
        }
        catch (IOException ex)
        {
            return new AcquisitionStepOutcome.Fail($"Could not move the files into place: {ex.Message}", ex);
        }

        ctx.Logger.LogInformation("[Acquisition] Placed resource {ResourceId} at {Target}",
            item.ResourceId, target);
        await ctx.ReportProgress(100, name);

        return new AcquisitionStepOutcome.Continue(item with
        {
            WorkingName = name,
            TargetDirectory = target,
        });
    }

    /// <summary>
    /// Where the files to move are: what unpacking produced, else the run's working directory.
    /// </summary>
    private static string? ResolveSource(AcquisitionWorkItem item, string workingDirectory)
    {
        foreach (var candidate in new[] {item.ExtractedDirectory, workingDirectory})
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) &&
                Directory.EnumerateFileSystemEntries(candidate).Any())
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// An archive that held one folder leaves a folder inside a folder. When the source contains
    /// exactly one directory and nothing else, that directory is what should be filed — otherwise
    /// the library fills up with shells named after download folders.
    /// </summary>
    private static string Collapse(string source)
    {
        var entries = Directory.GetFileSystemEntries(source);

        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : source;
    }

    private static void MoveInto(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var entry in Directory.GetFileSystemEntries(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(entry));

            if (Directory.Exists(entry))
            {
                if (Directory.Exists(destination))
                {
                    // Merging into a folder that already has one of the same name.
                    MoveInto(entry, destination);
                    Directory.Delete(entry, true);
                }
                else
                {
                    Directory.Move(entry, destination);
                }
            }
            else
            {
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(entry, destination);
            }
        }
    }

    private static string Deduplicate(string path)
    {
        for (var i = 2;; i++)
        {
            var candidate = $"{path} ({i})";

            if (!Directory.Exists(candidate)) return candidate;
        }
    }
}
