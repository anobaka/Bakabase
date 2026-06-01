namespace Bakabase.Service.Components.Subscription.Providers.Pixiv;

/// <summary>
/// Settings for the Pixiv "follow latest" feed (illustrations posted by users you follow).
/// </summary>
public record PixivFollowLatestTarget
{
    /// <summary>"all" (default) or "r18".</summary>
    public string Mode { get; init; } = "all";
}
