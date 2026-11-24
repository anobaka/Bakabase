using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.CloudDownloads.Rclone;

namespace Bakabase.Service.Components.DownloadIntents;

public sealed record DownloadSourceResolution(string Url, IReadOnlyList<string> Chain);

/// <summary>Only explicitly reviewed source shapes may cause discovery-time network requests.</summary>
public interface IDownloadSourceResolver
{
    bool CanResolve(Uri source);
    Task<DownloadSourceResolution> ResolveAsync(Uri source, CancellationToken ct);
}

public sealed class DownloadSourceResolver(IEnumerable<IDownloadSourceResolver> resolvers)
{
    private readonly IReadOnlyList<IDownloadSourceResolver> _resolvers = resolvers.ToList();

    public async Task<CloudSource> ResolveAsync(DownloadCandidate candidate, CancellationToken ct)
    {
        // Validate before any network request, deferring provider/method assertions until expansion.
        var unresolved = Normalize(candidate with {ProviderId = candidate.Kind == "remote" ? candidate.ProviderId : null, MethodId = null});
        var resolution = unresolved.Kind == "remote"
            ? new DownloadSourceResolution(unresolved.Url, [])
            : await ResolveAsync(unresolved.Url, ct);
        var resolvedCandidate = candidate with {Url = resolution.Url};
        // A Location header discovers a page, not a verified direct-file capability.
        // Persist that distinction so a later resume cannot promote its suffix to HTTP/BT.
        if (resolution.Chain.Count > 0 &&
            CloudSourceClassifier.Classify(resolution.Url) is "unknown" or "http" or "torrent")
            resolvedCandidate = resolvedCandidate with {Kind = "share"};
        var resolved = Normalize(resolvedCandidate);
        return resolution.Chain.Count == 0 ? resolved : resolved with
            {OriginalUrl = unresolved.Url, ResolutionChain = resolution.Chain.ToList()};
    }

    /// <summary>A persisted final locator stays fixed when a workflow resumes; provenance is not an alternate input.</summary>
    public async Task<CloudSource> ResolveAsync(CloudSource source, CancellationToken ct)
    {
        if (source.ResolutionChain == null)
            throw new ArgumentException("The source resolution provenance is invalid.");
        var candidate = new DownloadCandidate {Id = source.Id, Url = source.Url, Kind = source.Kind,
            ProviderId = source.ProviderId == "unknown" ? null : source.ProviderId, AccessCode = source.AccessCode,
            ArchivePassword = source.ArchivePassword, ExtractionPlanJson = source.ExtractionPlanJson,
            GroupId = source.GroupId, Version = source.Version, MethodId = source.MethodId, AccountId = source.AccountId};
        if (source.OriginalUrl == null && source.ResolutionChain.Count == 0)
        {
            var resolved = await ResolveAsync(candidate, ct);
            resolved = resolved with {AccountVersion = source.AccountVersion, AccessCodeRevisions = source.AccessCodeRevisions};
            if (resolved.ResolutionChain.Count != 0) return resolved;
            resolved = resolved with {ResolutionChain = source.ResolutionChain};
            return resolved == source ? source : resolved;
        }
        if (source.OriginalUrl == null || source.ResolutionChain.Count is < 2 or > HttpRedirectSourceResolver.MaximumRedirects + 1 ||
            source.ResolutionChain[0] != source.OriginalUrl || source.ResolutionChain[^1] != source.Url)
            throw new ArgumentException("The source resolution provenance is incomplete.");
        var scheme = new Uri(source.OriginalUrl, UriKind.Absolute).Scheme;
        foreach (var url in source.ResolutionChain)
        {
            var uri = new Uri(url, UriKind.Absolute);
            PublicCloudHttp.EnsureSafeUri(uri);
            if (uri.Scheme != scheme) throw new ArgumentException("The source resolution provenance changes protocols.");
        }
        return Normalize(candidate) with {OriginalUrl = source.OriginalUrl, ResolutionChain = source.ResolutionChain,
            AccountVersion = source.AccountVersion, AccessCodeRevisions = source.AccessCodeRevisions};
    }

    public static CloudSource Normalize(DownloadCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Id) || candidate.Id.Length > 160 ||
            string.IsNullOrWhiteSpace(candidate.Url) || candidate.Url.Length > 2048 ||
            candidate.AccessCode?.Length > 512 || candidate.ArchivePassword?.Length > 2048 ||
            candidate.ExtractionPlanJson?.Length > 131072 || candidate.Title?.Length > 1024 ||
            candidate.GroupId?.Length > 160 || candidate.Version?.Length > 1024 || candidate.MethodId?.Length > 160 ||
            candidate.AccountId != null && !Guid.TryParseExact(candidate.AccountId, "N", out _))
            throw new ArgumentException("The source or its access information is invalid or too long.");
        if (candidate.Kind is not ("auto" or "share" or "http" or "torrent" or "remote"))
            throw new ArgumentException("Choose a supported source type.");
        if (candidate.ExtractionPlanJson != null)
        {
            try { FileProcessingPlanValidator.Validate(JsonSerializer.Deserialize<FileProcessingPlan>(candidate.ExtractionPlanJson, JsonSerializerOptions.Web)); }
            catch (JsonException error) { throw new ArgumentException("The file-processing plan is invalid JSON.", error); }
        }
        var provider = "unknown";
        var kind = "share";
        if (candidate.Kind == "remote")
        {
            if (!DownloadMethodCatalog.Methods.Any(m => m.ProviderId == candidate.ProviderId && m.SupportsRemote) ||
                !candidate.Url.StartsWith('/') || candidate.Url.Any(char.IsControl) || candidate.Url.Contains(':') || candidate.Url.Contains('\\') ||
                candidate.Url.Split('/').Any(p => p is ".." or "."))
                throw new ArgumentException("Choose a supported source and an absolute cloud path.");
            provider = candidate.ProviderId!; kind = "remote";
        }
        else
        {
            if (!Uri.TryCreate(candidate.Url.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https" or "magnet") || uri.UserInfo.Length != 0 || candidate.Url.Contains('\0'))
                throw new ArgumentException("Provide an HTTP(S) URL or magnet without embedded credentials.");
            provider = CloudSourceClassifier.Classify(uri.AbsoluteUri);
            if (candidate.Kind == "share" && provider is "http" or "torrent")
                provider = "unknown";
            if (uri.Scheme != "magnet" && (uri.IdnHost is "e-hentai.org" or "exhentai.org") &&
                Regex.IsMatch(uri.AbsolutePath, @"^/g/[1-9][0-9]*/[a-zA-Z0-9]+/?$")) provider = "exHentai";
            if (candidate.Kind == "http")
            {
                if (provider is not ("unknown" or "http")) throw new ArgumentException("Use the source's site adapter instead of treating its page as a file.");
                provider = "http";
            }
            if (candidate.Kind == "torrent")
            {
                if (uri.Scheme is not ("http" or "https") || provider is not ("unknown" or "torrent"))
                    throw new ArgumentException("Provide a torrent metadata URL.");
                provider = "torrent";
            }
            kind = provider is "http" or "torrent" ? provider : "share";
        }
        var source = new CloudSource {Id = candidate.Id, Url = candidate.Url.Trim(), ProviderId = provider,
            Kind = kind, AccessCode = candidate.AccessCode, ArchivePassword = candidate.ArchivePassword,
            ExtractionPlanJson = candidate.ExtractionPlanJson, GroupId = candidate.GroupId, Version = candidate.Version,
            MethodId = candidate.MethodId, AccountId = candidate.AccountId};
        // Older saved plans use "unknown" when the URL had no adapter yet. It is
        // an unresolved placeholder, not an explicit provider choice to enforce.
        if (candidate.ProviderId is not (null or "unknown") && candidate.ProviderId != provider)
            throw new ArgumentException("The address does not belong to the selected download source.");
        if (candidate.MethodId != null && DownloadMethodCatalog.Select(source) == null)
            throw new ArgumentException("The selected method does not support this source.");
        var method = DownloadMethodCatalog.Select(source);
        if (kind == "remote" && method?.EngineId == "rclone")
        {
            try { _ = RcloneRemoteEngine.Remote(source.Url); }
            catch (RcloneRemoteException error) {throw new ArgumentException("Choose an account path without empty, reserved or unsafe path segments.", error);}
        }
        // Preserve legacy default-method payloads/checkpoints. A different engine selected by
        // capability (for example a public Filesystem directory) is always pinned explicitly.
        return source with {MethodId = candidate.MethodId ??
            (method?.Id != DownloadMethodCatalog.Find(provider)?.Id ? method?.Id : null), MethodScope = method?.ArtifactScope};
    }

    // A registered short-link resolver can discover a method even when the original
    // address has no descriptor. Projections can ask without making a network request.
    internal bool MayHaveDownloadMethod(CloudSource source) =>
        DownloadMethodCatalog.FindAll(source).Count > 0 || CanResolveUrl(source.Url);

    internal bool CanResolveUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && FindResolver(uri) != null;

    private IDownloadSourceResolver? FindResolver(Uri source) => _resolvers.FirstOrDefault(r => r.CanResolve(source));

    public async Task<DownloadSourceResolution> ResolveAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return new(url, []);
        var resolver = FindResolver(uri);
        return resolver == null ? new(url, []) : await resolver.ResolveAsync(uri, ct);
    }
}

/// <summary>HTTP Location only: no scripts, forms, guessed HTML download links or arbitrary URL fetches.</summary>
public sealed class HttpRedirectSourceResolver : IDownloadSourceResolver, IDisposable
{
    public const int MaximumRedirects = 5;
    private readonly HttpClient _http;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<Uri, bool> _usesProxy;

    public HttpRedirectSourceResolver() : this(new HttpClient(PublicCloudHttp.CreateHandler()),
        PublicCloudHttp.ResolvePublicAddressesAsync) { }

    public HttpRedirectSourceResolver(BakabaseWebProxy proxy) : this(
        proxy.ForDownloadTool(DownloadToolProxyTargets.SourceResolver)) { }

    private HttpRedirectSourceResolver(IWebProxy proxy) : this(
        new HttpClient(new ProxyAwarePublicHttpHandler(proxy, IsReviewedDestination, followRedirects: false)),
        PublicCloudHttp.ResolvePublicAddressesAsync,
        uri => !proxy.IsBypassed(uri) && proxy.GetProxy(uri) is { } endpoint && endpoint != uri) { }

    public HttpRedirectSourceResolver(HttpClient http,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve, Func<Uri, bool>? usesProxy = null)
    {
        _http = http;
        _resolve = resolve;
        _usesProxy = usesProxy ?? (_ => false);
    }

    public bool CanResolve(Uri source) => source.IsAbsoluteUri &&
        (source.IdnHost is "cm1.hk" or "cm2.hk" && Regex.IsMatch(source.AbsolutePath, @"^/s/[A-Za-z0-9_-]+/?$") ||
         source.IdnHost == "1drv.ms" && Regex.IsMatch(source.AbsolutePath, @"^/[A-Za-z]/[^/]+/?$"));

    private static bool IsReviewedDestination(Uri uri) => !IPAddress.TryParse(uri.IdnHost, out _) &&
        (uri.IdnHost is "cm1.hk" or "cm2.hk" or "1drv.ms" ||
         CloudSourceClassifier.Classify(uri.AbsoluteUri) is not ("unknown" or "http" or "torrent" or "magnet"));

    public async Task<DownloadSourceResolution> ResolveAsync(Uri source, CancellationToken ct)
    {
        if (!CanResolve(source)) throw new ArgumentException("This source has no reviewed link resolver.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var current = source;
        var chain = new List<string> {source.AbsoluteUri};
        try
        {
            for (var redirects = 0; ; redirects++)
            {
                PublicCloudHttp.EnsureSafeUri(current);
                if (IPAddress.TryParse(current.IdnHost, out _) ||
                    !IsReviewedDestination(current) && (redirects == 0 || current.Scheme != "https"))
                    throw new ArgumentException("The short link points to an unsupported page. Open it manually to choose a download source.");
                if (!_usesProxy(current))
                {
                    var addresses = await _resolve(current.IdnHost, timeout.Token);
                    if (addresses.Length == 0 || addresses.Any(a => !PublicCloudHttp.IsPublicAddress(a)))
                        throw new CloudSourceNetworkException();
                }
                // Preserve a safe terminal locator even when no method supports it. Only reviewed
                // short links are fetched; terminal HTML and direct-looking suffixes are never probed.
                if (redirects > 0 && !CanResolve(current))
                    return new(current.AbsoluteUri, chain);
                if (!CanResolve(current))
                    throw new ArgumentException("The short link points to an unsupported page. Open it manually to choose a download source.");
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                    HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects >= MaximumRedirects || response.Headers.Location is not { } location)
                        throw new ArgumentException("The short link has too many redirects or no redirect destination.");
                    var next = new Uri(current, location);
                    PublicCloudHttp.EnsureSafeUri(next);
                    if (next.Scheme != source.Scheme)
                        throw new ArgumentException("A short-link redirect cannot change protocols.");
                    if (chain.Contains(next.AbsoluteUri, StringComparer.Ordinal))
                        throw new ArgumentException("The short link contains a redirect loop.");
                    chain.Add(next.AbsoluteUri);
                    current = next;
                    continue;
                }
                throw new ArgumentException("The short link did not resolve to a supported download source. Complete its browser interaction manually.");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new InvalidOperationException("Resolving the short link timed out. Try again or provide its final share URL."); }
        catch (CloudSourceNetworkException) { throw; }
        catch (HttpRequestException error)
        { throw new InvalidOperationException("The short link could not be resolved. Try again or provide its final share URL.", error); }
    }

    public void Dispose() => _http.Dispose();
}
