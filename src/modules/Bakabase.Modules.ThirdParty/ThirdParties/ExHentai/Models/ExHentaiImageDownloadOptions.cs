using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;

/// <summary>An immutable account snapshot shared by balance checks and original-image requests.</summary>
public sealed class ExHentaiRequestContext
{
    public ExHentaiRequestContext(string cookie, string? accountKey = null)
    {
        Cookie = cookie ?? string.Empty;
        AccountKey = accountKey ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Cookie)))[..24];
    }

    [JsonIgnore] public string Cookie { get; }
    public string AccountKey { get; }
}

public sealed class ExHentaiImageDownloadOptions
{
    public bool PreferOriginal { get; init; }
    [JsonIgnore] public ExHentaiRequestContext? RequestContext { get; init; }
    [JsonIgnore] public Func<ExHentaiOriginalImageInfo, CancellationToken, Task>? BeforeOriginalDownload { get; init; }
    /// <summary>Rechecks spending immediately after HTTP pacing, without performing another request.</summary>
    [JsonIgnore] public Func<ExHentaiOriginalImageInfo, CancellationToken, Task>? BeforeOriginalSend { get; init; }
    /// <summary>Allows one original-node replacement only while its requests remain provably free. No HTTP work.</summary>
    [JsonIgnore] public Func<ExHentaiOriginalImageInfo, bool>? CanRecoverOriginalWithoutGp { get; init; }
}

public sealed class ExHentaiOriginalImageInfo
{
    public required string OriginalUrl { get; init; }
    public long? OriginalSizeBytes { get; init; }
    public string? PageUrl { get; init; }
    /// <summary>Trusted image-page Date header advanced with monotonic elapsed time; null when unavailable.</summary>
    public DateTime? ServerTimeUtc { get; init; }
}

public sealed class ExHentaiDownloadedImage
{
    public required byte[] Data { get; init; }
    public string? ContentType { get; init; }
    public bool IsOriginal { get; init; }
    public bool OriginalUnavailable { get; init; }
}

public sealed class ExHentaiAccountBalance
{
    public long GpBalance { get; init; }
    public long? CreditsBalance { get; init; }
}
