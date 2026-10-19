using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.Helpers;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>
/// Reads any public web page. It knows nothing about the site's markup — it takes the body and
/// keeps embedded source data that may contain links or credentials. A language model can find
/// links and codes in, and it means a link shared somewhere Bakabase has never heard of is still
/// worth following.
/// </summary>
public class GenericHtmlReader(IHttpClientFactory httpClientFactory, ILogger<GenericHtmlReader> logger)
    : ISharedContentReader
{
    public PostParserSource? Source => null;

    /// <summary>Anything that knows the site should win.</summary>
    public int Priority => 10;

    public bool CanRead(string reference) =>
        Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public async Task<PostContent> ReadAsync(string reference, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(GenericHtmlReader));
        var html = await client.GetStringAsync(reference, ct);

        var cq = new CQ(html);

        var title = cq["title"].Text()?.Trim();
        var body = HtmlContentSnapshot.Capture(cq["body"]);

        if (string.IsNullOrWhiteSpace(body))
        {
            logger.LogWarning("[SharedContent] {Reference} had no body to read", reference);
            body = html;
        }

        return new PostContent
        {
            Title = string.IsNullOrWhiteSpace(title) ? reference : title,
            MainHtml = body,
            SourceUrl = reference,
            CapturedAt = DateTimeOffset.UtcNow,
            Scope = "page",
            HtmlEncodingTrusted = true,
        };
    }
}

/// <summary>
/// Reads what the user pasted. There is no fetching to do: the reference is the content — a block
/// of text out of a chat, a mail, a document row.
/// </summary>
public class PlainTextReader : ISharedContentReader
{
    /// <summary>
    /// A reference the readers above would not touch: not a URL, and long enough to be content
    /// rather than a stray word.
    /// </summary>
    public const int MinimumLength = 8;

    public PostParserSource? Source => null;

    /// <summary>The last resort.</summary>
    public int Priority => 0;

    public bool CanRead(string reference) =>
        !string.IsNullOrWhiteSpace(reference) &&
        reference.Trim().Length >= MinimumLength &&
        !Uri.TryCreate(reference.Trim(), UriKind.Absolute, out _);

    public Task<PostContent> ReadAsync(string reference, CancellationToken ct)
    {
        var text = reference;
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries) is [var head, ..]
            ? head.Trim()
            : text;

        return Task.FromResult(new PostContent
        {
            Title = firstLine.Length > 120 ? firstLine[..120] : firstLine,
            MainHtml = text,
            CapturedAt = DateTimeOffset.UtcNow,
            Scope = "pastedText",
        });
    }
}
