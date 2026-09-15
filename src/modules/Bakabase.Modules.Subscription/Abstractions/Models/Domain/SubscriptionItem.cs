namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

/// <summary>
/// One entry a source is currently listing.
/// </summary>
/// <param name="SourceKey">
/// For a platform or catalog source, the site's work identifier, used to match an external identity
/// or platform source link. For a sharing channel, a key unique within the channel
/// (a thread id) used only to tell one act of sharing from another.
/// </param>
/// <param name="Title">What the source calls it. Becomes the resource's name when one is created.</param>
/// <param name="Url">
/// The item's page. For a sharing channel this is also the lead — the link someone would follow to
/// get the thing.
/// </param>
/// <param name="CoverUrls">Cover images the source offers, best first.</param>
/// <param name="MetadataJson">
/// The site's own fields, kept verbatim on the external identity or platform source link,
/// where a site-specific reader can make sense of them later.
/// </param>
public record SubscriptionItem(
    string SourceKey,
    string? Title = null,
    string? Url = null,
    List<string>? CoverUrls = null,
    string? MetadataJson = null);
