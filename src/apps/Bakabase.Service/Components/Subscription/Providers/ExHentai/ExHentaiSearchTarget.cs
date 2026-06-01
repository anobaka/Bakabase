namespace Bakabase.Service.Components.Subscription.Providers.ExHentai;

public record ExHentaiSearchTarget
{
    /// <summary>Full ExHentai / E-hentai list URL — e.g. a saved search or the Watched feed.</summary>
    public string Url { get; init; } = "";
}
