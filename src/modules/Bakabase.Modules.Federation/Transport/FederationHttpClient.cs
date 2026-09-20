using System.Net.Http.Headers;
using System.Text.Json;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Transport;

/// <summary>The registered client disables redirects and cookies; every exchange has one explicit target.</summary>
public sealed class FederationHttpClient(HttpClient http)
{
    public const int MaxControlResponseBytes = 4 * 1024 * 1024;

    public static string NormalizeAddress(string address)
    {
        var value = address?.Trim().TrimEnd('/') ?? "";
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new FederationAccessException("InvalidAddress", 400, "Use the node's HTTP or HTTPS host and port, without a path or credentials.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task<T> PublicAsync<T>(string address, HttpMethod method, string path, object? body,
        CancellationToken ct)
    {
        using var request = CreateRequest(address, method, path, body);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await SendAsync(request, deadline.Token);
        return await ReadEnvelopeAsync<T>(response, deadline.Token);
    }

    public static HttpRequestMessage CreateRequest(string address, HttpMethod method, string relativePath, object? body)
    {
        if (!relativePath.StartsWith("/federation/v1/", StringComparison.Ordinal) || relativePath.Contains('#'))
            throw new FederationAccessException("InvalidNodeRoute", 400, "Only explicit federation protocol routes may be requested.");
        var root = new Uri(NormalizeAddress(address));
        var destination = new Uri(root, relativePath);
        if (destination.Authority != root.Authority || !destination.AbsolutePath.StartsWith("/federation/v1/", StringComparison.Ordinal))
            throw new FederationAccessException("InvalidNodeRoute", 400, "The node route escaped its protocol namespace.");
        var request = new HttpRequestMessage(method, destination);
        if (body != null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, FederationJson.Options);
            if (bytes.Length > NodeRequestSignature.MaxControlBodyBytes)
            {
                request.Dispose();
                throw new FederationAccessException("RequestTooLarge", 413, "The node control request is too large.");
            }
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            throw new FederationAccessException("NodeUnreachable", 503, "The source node could not be reached.");
        }
    }

    public static async Task<T> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new FederationAccessException("NodeRedirectRefused", 502, "The node redirected a protocol request. Verify its address.");
        var bytes = await ReadBoundedAsync(response.Content, MaxControlResponseBytes, ct);
        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            try
            {
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.TryGetProperty("code", out var error) && error.ValueKind == JsonValueKind.String)
                    code = error.GetString();
            }
            catch (JsonException) { }
            throw new FederationAccessException(code ?? "NodeRequestRefused", (int)response.StatusCode,
                "The source node refused this request. Check its sharing permission and availability.");
        }
        try
        {
            using var json = JsonDocument.Parse(bytes);
            return json.RootElement.Deserialize<T>(FederationJson.Options) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new FederationAccessException("InvalidNodeResponse", 502, "The source node returned an invalid protocol response.");
        }
    }

    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
            throw new FederationAccessException("NodeResponseTooLarge", 502, "The source node exceeded the response budget.");
        await using var source = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(chunk, ct);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maxBytes)
                throw new FederationAccessException("NodeResponseTooLarge", 502, "The source node exceeded the response budget.");
            buffer.Write(chunk, 0, count);
        }
    }

    public static async Task SignAsync(HttpRequestMessage request, NodeCredentials credentials,
        DateTimeOffset now, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        if (body.Length > NodeRequestSignature.MaxControlBodyBytes)
            throw new FederationAccessException("RequestTooLarge", 413, "The node control request is too large.");
        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", NodeRequestSignature.Create(credentials,
            request.Method.Method, uri.AbsolutePath, uri.Query.Length == 0 ? "" : uri.Query[1..], NodeRequestSignature.Hash(body), now));
    }
}
