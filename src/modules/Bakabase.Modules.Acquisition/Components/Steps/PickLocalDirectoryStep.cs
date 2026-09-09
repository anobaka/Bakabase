using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Components.Steps;

/// <summary>
/// Asks the user to point at a folder they already have.
/// <para>
/// The case where nothing needs getting at all: the files have been on disk for years and the
/// library simply does not know about them. It is a recipe rather than a special path through the
/// code, so the same renaming and placing steps apply to it as to a download.
/// </para>
/// </summary>
public class PickLocalDirectoryStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.PickLocalDirectory;
    public string DisplayName => "Point at a local folder";
    public Type? ConfigType => null;

    /// <summary>What the interface should ask about.</summary>
    public record Prompt(string? Title, int ResourceId);

    /// <summary>The answer: where the files are.</summary>
    public record DirectorySignal(string Directory);

    public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.ExtractedDirectory) &&
            Directory.Exists(item.ExtractedDirectory))
        {
            // Already answered — a re-run at the cursor after a restart finds its own answer.
            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(item));
        }

        return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Suspend(
            AcquisitionWaitReason.PickDirectory,
            JsonSerializer.Serialize(new Prompt(item.Title, item.ResourceId), Json),
            item));
    }

    public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        AcquisitionResumeSignal signal, CancellationToken ct)
    {
        DirectorySignal? answer = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { answer = JsonSerializer.Deserialize<DirectorySignal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return Task.FromResult<AcquisitionStepOutcome>(
                    new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}"));
            }
        }

        var directory = answer?.Directory;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult<AcquisitionStepOutcome>(
                new AcquisitionStepOutcome.Fail($"\"{directory}\" is not a folder that exists."));
        }

        return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(
            item with {ExtractedDirectory = Path.GetFullPath(directory)}));
    }
}
