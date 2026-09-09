using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Modules.Acquisition.Components.Steps;

/// <summary>
/// Decides which of the links found in the shared content to actually use.
/// <para>
/// A post usually offers the same files on three drives, and which one is best depends entirely on
/// where the user has an account and what is fast where they live. So the choice is a preference
/// list, not a rule — and when the preferences say nothing useful, asking is better than guessing.
/// </para>
/// </summary>
public class SelectLinkStep(IBOptions<AcquisitionOptions> options) : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.SelectLink;
    public string DisplayName => "Choose a link";
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        /// <summary>
        /// Always stop and let the user pick, even when the preferences give a clear winner. For
        /// someone who wants to see what a post offered before anything is downloaded.
        /// </summary>
        public bool AlwaysAsk { get; init; }
    }

    /// <summary>What the interface is asked to show while the run waits.</summary>
    public record Prompt(IReadOnlyList<PromptLink> Links, string? Why = null);

    public record PromptLink(int Index, string Url, AcquisitionDriveKind DriveKind, string? AccessCode);

    /// <summary>
    /// The answer. Either an index into the links the run already had, or links the user found
    /// themselves — which is what makes <see cref="AcquisitionWaitReason.NoLinks"/> recoverable
    /// rather than a dead end.
    /// </summary>
    public record Signal(int? SelectedIndex, IReadOnlyList<AcquisitionLink>? Links);

    public Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        CancellationToken ct)
    {
        if (item.Links.Count == 0)
        {
            // Not a failure: the user can paste a link they found themselves and the run carries on.
            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Suspend(
                AcquisitionWaitReason.NoLinks,
                JsonSerializer.Serialize(new Prompt([], "Nothing downloadable was found in the shared content."),
                    Json),
                item));
        }

        var config = ctx.GetConfig<Config>();

        if (config?.AlwaysAsk == true)
        {
            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Suspend(
                AcquisitionWaitReason.ChooseLink, PromptFor(item), item));
        }

        var index = Prefer(item.Links);

        return Task.FromResult<AcquisitionStepOutcome>(
            new AcquisitionStepOutcome.Continue(item with {SelectedLinkIndex = index}));
    }

    public Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        AcquisitionResumeSignal signal, CancellationToken ct)
    {
        Signal? answer = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { answer = JsonSerializer.Deserialize<Signal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return Task.FromResult<AcquisitionStepOutcome>(
                    new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}"));
            }
        }

        var links = answer?.Links is {Count: > 0} supplied ? supplied : item.Links;

        if (links.Count == 0)
        {
            return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Fail(
                "There is still nothing to download, and no link was supplied."));
        }

        var index = answer?.SelectedIndex is { } chosen && chosen >= 0 && chosen < links.Count
            ? chosen
            : Prefer(links);

        return Task.FromResult<AcquisitionStepOutcome>(new AcquisitionStepOutcome.Continue(
            item with {Links = links, SelectedLinkIndex = index}));
    }

    private string PromptFor(AcquisitionWorkItem item) =>
        JsonSerializer.Serialize(new Prompt(item.Links
            .Select((l, i) => new PromptLink(i, l.Url, l.DriveKind, l.AccessCode))
            .ToList()), Json);

    /// <summary>
    /// First link whose service appears earliest in the user's preference list; failing that, the
    /// first link at all. Order within the post is meaningful — authors tend to put the one they
    /// recommend first.
    /// </summary>
    private int Prefer(IReadOnlyList<AcquisitionLink> links)
    {
        var preferences = options.Value.PreferredDriveKinds;

        for (var rank = 0; rank < preferences.Count; rank++)
        {
            for (var i = 0; i < links.Count; i++)
            {
                if (links[i].DriveKind == preferences[rank]) return i;
            }
        }

        return 0;
    }
}
