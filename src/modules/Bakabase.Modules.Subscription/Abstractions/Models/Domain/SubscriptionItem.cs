namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain;

/// <summary>
/// One entry a source is currently listing.
/// </summary>
/// <param name="SourceKey">
/// For a platform or catalog source, the same key a <c>ResourceSourceLink</c> stores, so an item
/// and a resource can recognise each other. For a sharing channel, a key unique within the channel
/// (a thread id) used only to tell one act of sharing from another.
/// </param>
/// <param name="Title">What the source calls it. Becomes the resource's name when one is created.</param>
/// <param name="Url">
/// The item's page. For a sharing channel this is also the lead — the link someone would follow to
/// get the thing.
/// </param>
/// <param name="CoverUrls">Cover images the source offers, best first.</param>
/// <param name="MetadataJson">
/// The source's own fields, kept verbatim. Stored on the resource's source link, where a
/// platform-specific reader can make sense of it later.
/// </param>
public record SubscriptionItem(
    string SourceKey,
    string? Title = null,
    string? Url = null,
    List<string>? CoverUrls = null,
    string? MetadataJson = null);
