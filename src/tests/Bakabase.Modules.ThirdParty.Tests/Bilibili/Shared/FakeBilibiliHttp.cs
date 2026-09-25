using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Shared by Bakabase.Modules.ThirdParty.Tests (the page service) and Bakabase.Tests (the downloader end to end),
// which links this file: one fake of the wire, so both suites test the same protocol.
namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Shared;

/// <summary>One request as it went over the wire: which named client sent it, and whether it carried a cookie.</summary>
internal sealed record BilibiliHit(string Client, string Url, bool HasCookie, long? RangeFrom);

/// <summary>
/// Both Bilibili HTTP clients of the app, routed by client name as the app does: <c>"Bilibili"</c> (the API; its
/// handler attaches the account cookie to everything it sends, as the real one does, so a request routed through
/// the wrong client would carry it) to <see cref="Api"/>, <c>"BilibiliCdn"</c> to <see cref="Cdn"/>. Usable as an
/// <see cref="IHttpClientFactory"/> (any other name fails the test) or through <see cref="ApiHandler"/> and
/// <see cref="CdnHandler"/> as primary handlers of a real factory.
/// </summary>
internal sealed class FakeBilibiliHttp : IHttpClientFactory
{
    public const string CookieSentinel = "SENTINEL_COOKIE_VALUE";
    public const string Cookie = "SESSDATA=" + CookieSentinel;

    public FakeBilibiliHttp()
    {
        ApiHandler = new Recorder(this, InternalOptions.HttpClientNames.Bilibili, Api, true);
        CdnHandler = new Recorder(this, InternalOptions.HttpClientNames.BilibiliCdn, Cdn, false);
    }

    public FakeApi Api { get; } = new();
    public FakeCdn Cdn { get; } = new();
    public HttpMessageHandler ApiHandler { get; }
    public HttpMessageHandler CdnHandler { get; }
    public ConcurrentQueue<BilibiliHit> Hits { get; } = new();

    public HttpClient CreateClient(string name) => name switch
    {
        InternalOptions.HttpClientNames.Bilibili => new HttpClient(ApiHandler, false),
        InternalOptions.HttpClientNames.BilibiliCdn => new HttpClient(CdnHandler, false)
            {Timeout = Timeout.InfiniteTimeSpan},
        _ => throw new AssertFailedException($"Unexpected HTTP client '{name}'."),
    };

    private sealed class Recorder(FakeBilibiliHttp owner, string name, HttpMessageHandler inner, bool attachCookie)
        : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (attachCookie)
            {
                request.Headers.TryAddWithoutValidation("Cookie", Cookie);
            }

            owner.Hits.Enqueue(new BilibiliHit(name, request.RequestUri!.ToString(), request.Headers.Contains("Cookie"),
                request.Headers.Range?.Ranges.FirstOrDefault()?.From));
            return base.SendAsync(request, ct);
        }

        protected override void Dispose(bool disposing)
        {
            // HTTP client factories recycle handlers; this one outlives them.
        }
    }
}

/// <summary>
/// The API side. Answers are fixture names (<c>Fixtures/Bilibili</c>) or raw JSON, scripted per endpoint kind and
/// optionally per aid (<c>avid</c>/<c>aid</c>/<c>pid</c> query value) or, for favorites lists, per page; a queue per
/// key, whose last answer repeats. Unscripted requests answer HTTP 404.
/// </summary>
internal sealed class FakeApi : HttpMessageHandler
{
    public const string MyInfo = "myinfo";
    public const string Folders = "folders";
    public const string List = "list";
    public const string View = "view";
    public const string PageList = "pagelist";
    public const string Naming = "naming";
    public const string PlayUrl = "playurl";
    public const string DmView = "dmview";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _scripts = new();
    private readonly ConcurrentDictionary<string, string> _last = new();

    public ConcurrentQueue<(string Kind, long? Aid, int? Page, string Url)> Requests { get; } = new();

    public int Count(string kind, long? aid = null) =>
        Requests.Count(r => r.Kind == kind && (aid == null || r.Aid == aid));

    /// <summary>Replaces the script of <paramref name="kind"/>.</summary>
    public FakeApi On(string kind, params string[] answers) => Script(kind, answers);

    /// <summary>Replaces the script of <paramref name="kind"/> for one aid (wins over the kind's own script).</summary>
    public FakeApi On(string kind, long aid, params string[] answers) => Script($"{kind}:{aid}", answers);

    /// <summary>Favorites list pages, keyed by page number.</summary>
    public FakeApi OnListPage(int page, params string[] answers) => Script($"{List}:{page}", answers);

    private FakeApi Script(string key, string[] answers)
    {
        _last.TryRemove(key, out _);
        _scripts[key] = new ConcurrentQueue<string>(answers);
        return this;
    }

    public static string KindOf(Uri uri)
    {
        var query = uri.Query;
        return uri.AbsolutePath switch
        {
            "/x/space/v2/myinfo" => MyInfo,
            "/x/v3/fav/folder/created/list-all" => Folders,
            "/x/v3/fav/resource/list" => List,
            "/x/web-interface/view" => View,
            "/x/player/pagelist" => PageList,
            "/x/player/playurl" when query.EndsWith("fnval=16") || query.Contains("fnval=16&") => Naming,
            "/x/player/playurl" when query.Contains("fnval=4048") => PlayUrl,
            "/x/v2/dm/view" => DmView,
            _ => "unknown",
        };
    }

    public static long? QueryLong(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == name && long.TryParse(pair[(eq + 1)..], out var value))
            {
                return value;
            }
        }

        return null;
    }

    private string? Next(string key)
    {
        if (_scripts.TryGetValue(key, out var queue) && queue.TryDequeue(out var next))
        {
            _last[key] = next;
            return next;
        }

        return _last.TryGetValue(key, out var last) ? last : null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        var kind = KindOf(uri);
        var aid = QueryLong(uri, "avid") ?? QueryLong(uri, "aid") ?? QueryLong(uri, "pid");
        var page = kind == List ? (int?) QueryLong(uri, "pn") : null;
        Requests.Enqueue((kind, aid, page, uri.ToString()));

        var answer = (page != null ? Next($"{kind}:{page}") : null) ??
                     (aid != null ? Next($"{kind}:{aid}") : null) ??
                     Next(kind);
        if (answer == null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {Content = new StringContent("no route")});
        }

        var body = answer.TrimStart().StartsWith('{')
            ? answer
            : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Bilibili", answer));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// The CDN side. Media files (<c>.m4s</c>, <c>.mp4</c>, <c>.flv</c>) are served by default with content derived from
/// their file name (<c>Range: bytes=N-</c> honoured); anything else answers 404 unless a route says otherwise
/// (<see cref="ServeDanmakuAndCovers"/> adds the usual ones). Routes added later win; a route returning null passes
/// to the ones before it.
/// </summary>
internal sealed class FakeCdn : HttpMessageHandler
{
    public const string DanmakuXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><i><d p=\"1,1,25,16777215,0,0,0,0\">弹幕</d></i>";

    private readonly List<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage?>> _routes = [];

    public ConcurrentQueue<string> Requests { get; } = new();

    public static string FileNameOf(string url) => new Uri(url).AbsolutePath.Split('/').Last();

    public int Count(string fileName) => Requests.Count(u => FileNameOf(u) == fileName);

    public static byte[] ContentOf(string fileName)
    {
        var seed = Encoding.UTF8.GetBytes(fileName + ";");
        var bytes = new byte[64 * 1024];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = seed[i % seed.Length];
        }

        return bytes;
    }

    public FakeCdn Route(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage?> route)
    {
        lock (_routes)
        {
            _routes.Add(route);
        }

        return this;
    }

    public FakeCdn File(string fileName, Func<HttpRequestMessage, CancellationToken, HttpResponseMessage?> respond) =>
        Route((r, ct) => FileNameOf(r.RequestUri!.ToString()) == fileName ? respond(r, ct) : null);

    /// <summary>The first <paramref name="times"/> requests for the file answer <paramref name="status"/>.</summary>
    public FakeCdn Fail(string fileName, HttpStatusCode status, int times = int.MaxValue)
    {
        var count = 0;
        return Route((r, _) =>
        {
            if (FileNameOf(r.RequestUri!.ToString()) != fileName || Interlocked.Increment(ref count) > times)
            {
                return null;
            }

            return new HttpResponseMessage(status) {Content = new ByteArrayContent([])};
        });
    }

    /// <summary>Danmaku (<c>comment.bilibili.com</c>, raw deflate) and <c>.jpg</c> covers, as small files.</summary>
    public FakeCdn ServeDanmakuAndCovers() => Route((request, _) =>
    {
        var uri = request.RequestUri!;
        if (uri.Host == "comment.bilibili.com")
        {
            var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true))
            {
                deflate.Write(Encoding.UTF8.GetBytes(DanmakuXml));
            }

            var danmaku = new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new ByteArrayContent(compressed.ToArray())};
            danmaku.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
            danmaku.Content.Headers.ContentEncoding.Add("deflate");
            return danmaku;
        }

        if (FileNameOf(uri.ToString()).EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            var cover = new HttpResponseMessage(HttpStatusCode.OK)
                {Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9])};
            cover.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return cover;
        }

        return null;
    });

    /// <summary>Serves <paramref name="content"/> honouring <c>Range: bytes=N-</c> (416 past the end).</summary>
    /// <param name="body">What actually goes over the wire, when not the content itself (e.g. a body that breaks
    /// off); the headers still describe <paramref name="content"/>.</param>
    public static HttpResponseMessage Serve(HttpRequestMessage request, byte[] content, Stream? body = null)
    {
        var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
        if (from > 0 && from >= content.Length)
        {
            var unsatisfiable = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                {Content = new ByteArrayContent([])};
            unsatisfiable.Content.Headers.ContentRange = new ContentRangeHeaderValue(content.Length);
            return unsatisfiable;
        }

        var slice = content.AsMemory((int) from).ToArray();
        var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = body != null ? new StreamContent(body) : new ByteArrayContent(slice),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        response.Content.Headers.ContentLength = slice.Length;
        if (from > 0)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
        }

        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var url = request.RequestUri!.ToString();
        Requests.Enqueue(url);
        List<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage?>> routes;
        lock (_routes)
        {
            routes = _routes.ToList();
        }

        for (var i = routes.Count - 1; i >= 0; i--)
        {
            if (routes[i](request, ct) is { } routed)
            {
                return Task.FromResult(routed);
            }
        }

        var fileName = FileNameOf(url);
        return Task.FromResult(Path.GetExtension(fileName) is ".m4s" or ".mp4" or ".flv"
            ? Serve(request, ContentOf(fileName))
            : new HttpResponseMessage(HttpStatusCode.NotFound) {Content = new ByteArrayContent([])});
    }
}
