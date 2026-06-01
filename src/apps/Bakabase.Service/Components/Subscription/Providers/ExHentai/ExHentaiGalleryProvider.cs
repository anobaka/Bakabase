using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

namespace Bakabase.Service.Components.Subscription.Providers.ExHentai;

/// <summary>
/// Watches a single ExHentai gallery and emits a notification when its detail changes
/// (page count, tags, or upload timestamp). The snapshot stores one item whose
/// RawPayloadJson carries a fingerprint of the gallery state.
/// </summary>
public class ExHentaiGalleryProvider : ISubscriptionProvider
{
    private readonly ExHentaiClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ExHentaiGalleryProvider(ExHentaiClient client)
    {
        _client = client;
    }

    public string Kind => "exhentai.gallery";
    public string DisplayName => "ExHentai Gallery";
    public string? Icon => null;

    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct)
    {
        var target = TryParse(targetJson);
        if (target is null || string.IsNullOrWhiteSpace(target.Url))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("Gallery URL is required"));
        }
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(SubscriptionValidationResult.Invalid("URL must be an absolute http(s) URL"));
        }
        return Task.FromResult(SubscriptionValidationResult.Valid);
    }

    public string DescribeTarget(string targetJson) =>
        TryParse(targetJson)?.Url ?? "";

    public async Task<SubscriptionCheckResult> CheckAsync(
        SubscriptionRecord subscription,
        SubscriptionSnapshot? lastSnapshot,
        CancellationToken ct)
    {
        var target = TryParse(subscription.TargetJson)
            ?? throw new InvalidOperationException("Invalid target payload");

        var detail = await _client.ParseDetail(target.Url, includeTorrents: false);
        var currentFingerprint = Fingerprint(detail);

        var item = new SubscriptionItem(
            Id: detail.Id.ToString(),
            Title: string.IsNullOrEmpty(detail.Name) ? detail.RawName : detail.Name,
            Url: detail.Url,
            ThumbnailUrl: detail.CoverUrl,
            RawPayloadJson: JsonSerializer.Serialize(new GalleryFingerprintPayload
            {
                Fingerprint = currentFingerprint,
                PageCount = detail.PageCount,
                FileCount = detail.FileCount,
                UpdateDt = detail.UpdateDt,
                TagCount = detail.Tags?.Sum(g => g.Value.Length) ?? 0,
            }, JsonOptions));

        var lastItem = lastSnapshot?.Items.FirstOrDefault();
        var lastFingerprint = lastItem is null
            ? null
            : TryParsePayload(lastItem.RawPayloadJson)?.Fingerprint;

        IReadOnlyList<SubscriptionItem> updatedItems =
            (lastFingerprint is not null && lastFingerprint != currentFingerprint)
                ? [item]
                : [];

        return new SubscriptionCheckResult(
            new SubscriptionSnapshot { Items = [item] },
            NewItems: [],
            UpdatedItems: updatedItems);
    }

    private static string Fingerprint(global::Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.ExHentaiResource detail)
    {
        var sb = new StringBuilder();
        sb.Append(detail.PageCount).Append('|');
        sb.Append(detail.FileCount).Append('|');
        sb.Append(detail.UpdateDt.Ticks).Append('|');
        if (detail.Tags is not null)
        {
            foreach (var ns in detail.Tags.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                sb.Append(ns).Append('=');
                foreach (var v in detail.Tags[ns].OrderBy(s => s, StringComparer.Ordinal))
                {
                    sb.Append(v).Append(',');
                }
                sb.Append(';');
            }
        }
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static ExHentaiGalleryTarget? TryParse(string targetJson)
    {
        try
        {
            return JsonSerializer.Deserialize<ExHentaiGalleryTarget>(targetJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GalleryFingerprintPayload? TryParsePayload(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<GalleryFingerprintPayload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record GalleryFingerprintPayload
    {
        public string Fingerprint { get; init; } = "";
        public int PageCount { get; init; }
        public int FileCount { get; init; }
        public DateTime UpdateDt { get; init; }
        public int TagCount { get; init; }
    }
}
