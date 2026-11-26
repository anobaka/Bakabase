using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

/// <summary>The CLI owns device authorization and token refresh. This fixed Drive API operation
/// fills the gap between the files:purge scope and the CLI's trash-only file commands.</summary>
internal sealed class PikPakCliPurgeClient
{
    private static readonly Uri Endpoint = new("https://api-drive.mypikpak.com/drive/v1/files:batchDelete");
    private readonly Func<HttpClient> _client;

    public PikPakCliPurgeClient(BakabaseWebProxy proxy) : this(() => new(CreateHandler(proxy.ForDownloadTool("pikpak-cli", ThirdPartyId.PikPak))) {Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 64 * 1024}) { }

    internal PikPakCliPurgeClient(Func<HttpClient> client) => _client = client;

    internal static SocketsHttpHandler CreateHandler(IWebProxy proxy) => new()
    {
        Proxy = proxy, UseProxy = true, AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(10)
    };

    private sealed record Credential([property: JsonPropertyName("user_id")] string? UserId,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("token_expiry")] DateTimeOffset? Expires,
        [property: JsonPropertyName("device_id")] string? DeviceId, [property: JsonPropertyName("client_id")] string? ClientId);

    public async Task DeleteFilesAsync(string profile, string providerIdentity, JsonElement status, IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count is 0 or > 100 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count ||
            ids.Any(id => id.Length is 0 or > 128 || !char.IsAsciiLetterOrDigit(id[0]) ||
                id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
            throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");

        if (!PikPakCliJson.True(status, "logged_in")) throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
        if (PikPakCliAccountService.ReadProviderIdentity(status) != providerIdentity)
            throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
        var credentials = PikPakCliProcess.CredentialsPath(profile);
        if (!File.Exists(credentials) || new FileInfo(credentials).Length > 128 * 1024)
            throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
        Credential? credential;
        try
        {
            // Bind only access credentials; never deserialize or renew the CLI's refresh token.
            await using var stream = File.OpenRead(credentials);
            credential = await JsonSerializer.DeserializeAsync<Credential>(stream, cancellationToken: ct);
        }
        catch (JsonException) { throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED"); }
        if (!string.IsNullOrEmpty(credential?.UserId) && PikPakCliStorage.Hash(credential.UserId) != providerIdentity)
            throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
        if (credential == null || !HeaderValue(credential.AccessToken, 16384) || credential.Expires == null || credential.Expires <= DateTimeOffset.UtcNow ||
            !HeaderValue(credential.ClientId, 256) || !HeaderValue(credential.DeviceId, 256))
            throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
        using var client = _client();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) {Content = JsonContent.Create(new {ids})};
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Add("X-Client-Id", credential.ClientId);
        request.Headers.Add("X-Device-Id", credential.DeviceId);
        try
        {
            using var response = await client.SendAsync(request, ct);
            var code = "";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 && response.IsSuccessStatusCode) return;
            try
            {
                using var body = JsonDocument.Parse(bytes);
                if (body.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (!body.RootElement.TryGetProperty("error", out var error))
                    {
                        if (response.IsSuccessStatusCode) return; // The CLI independently verifies every file ID.
                    }
                    else code = error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : PikPakCliJson.Text(error, "code") ?? "";
                }
            }
            catch (JsonException) { }
            throw new PikPakCliException(code.ToUpperInvariant() switch
            {
                "CAPTCHA_REQUIRED" or "CAPTCHA_INVALID" or "CAPTCHA_TOKEN_REQUIRED" => "PIKPAK_VERIFICATION_REQUIRED",
                "INSUFFICIENT_SCOPE" or "PERMISSION_DENIED" => "PIKPAK_PURGE_FILES_SCOPE_REQUIRED",
                _ when response.StatusCode == HttpStatusCode.Unauthorized => "PIKPAK_ACCOUNT_REQUIRED",
                _ when response.StatusCode == HttpStatusCode.Forbidden => "PIKPAK_PURGE_FILES_SCOPE_REQUIRED",
                _ when (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests => "PIKPAK_NETWORK_UNAVAILABLE",
                _ => "PIKPAK_PURGE_UNAVAILABLE"
            });
        }
        catch (HttpRequestException) { throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE"); }
    }

    private static bool HeaderValue(string? value, int limit) => !string.IsNullOrEmpty(value) && value.Length <= limit &&
        value.All(c => c is >= '!' and <= '~');
}
