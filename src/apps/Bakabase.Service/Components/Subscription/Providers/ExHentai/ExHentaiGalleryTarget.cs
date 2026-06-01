namespace Bakabase.Service.Components.Subscription.Providers.ExHentai;

public record ExHentaiGalleryTarget
{
    /// <summary>Full ExHentai/E-hentai gallery URL — e.g. https://exhentai.org/g/123456/abcdef0123/.</summary>
    public string Url { get; init; } = "";
}
