using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.Network;

/// <summary>
/// DelegatingHandler that records each outgoing HTTP request when an
/// <see cref="HttpInteractionCapture"/> scope is active on the current async flow.
/// No-op (single null check) when no scope is active, so it's safe to leave attached
/// to all named clients in production.
/// </summary>
public class HttpInteractionCaptureHandler : DelegatingHandler
{
    private const int MaxBodyChars = 64 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var bucket = HttpInteractionCapture.Current;
        if (bucket == null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var sw = Stopwatch.StartNew();
        string? requestBody = null;
        string? requestContentType = null;
        if (request.Content != null)
        {
            try
            {
                await request.Content.LoadIntoBufferAsync();
                requestBody = await ReadAsTextAsync(request.Content);
                requestContentType = request.Content.Headers.ContentType?.ToString();
            }
            catch
            {
                requestBody = "<unreadable>";
            }
        }

        var requestHeaders = ExtractHeaders(request.Headers, request.Content?.Headers);
        var url = request.RequestUri?.ToString() ?? string.Empty;
        var method = request.Method.Method;

        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            sw.Stop();
            bucket.Add(new HttpInteractionRecord
            {
                Method = method,
                Url = url,
                RequestHeaders = requestHeaders,
                RequestBody = Truncate(requestBody),
                RequestContentType = requestContentType,
                ResponseStatusCode = (int)response.StatusCode,
                ResponseReasonPhrase = response.ReasonPhrase,
                ResponseHeaders = ExtractHeaders(response.Headers, response.Content?.Headers),
                ResponseContentType = response.Content?.Headers.ContentType?.ToString(),
                ResponseContentLength = response.Content?.Headers.ContentLength,
                DurationMs = sw.ElapsedMilliseconds,
            });
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            bucket.Add(new HttpInteractionRecord
            {
                Method = method,
                Url = url,
                RequestHeaders = requestHeaders,
                RequestBody = Truncate(requestBody),
                RequestContentType = requestContentType,
                Error = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
            });
            throw;
        }
    }

    private static Dictionary<string, string> ExtractHeaders(
        System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpHeaders? contentHeaders)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in headers)
        {
            dict[k] = string.Join(", ", v);
        }
        if (contentHeaders != null)
        {
            foreach (var (k, v) in contentHeaders)
            {
                dict[k] = string.Join(", ", v);
            }
        }
        return dict;
    }

    private static async Task<string?> ReadAsTextAsync(HttpContent content)
    {
        var contentType = content.Headers.ContentType?.MediaType;
        if (contentType != null && !LooksLikeText(contentType))
        {
            var len = content.Headers.ContentLength;
            return $"<binary {contentType}{(len.HasValue ? $", {len} bytes" : string.Empty)}>";
        }
        return await content.ReadAsStringAsync();
    }

    private static bool LooksLikeText(string mediaType)
    {
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
        if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)) return true;
        if (mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) return true;
        if (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return true;
        if (mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? Truncate(string? s)
    {
        if (s == null) return null;
        return s.Length <= MaxBodyChars
            ? s
            : s[..MaxBodyChars] + $"\n…<truncated, total {s.Length} chars>";
    }
}
