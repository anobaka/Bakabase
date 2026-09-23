using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Remoting.Components.Connection;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Asks the server what it makes of this client, and remembers the answer briefly.
/// </summary>
/// <remarks>
/// Cached because the frontend calls the context endpoint on every page it opens, and
/// the answer only changes when somebody flips a setting or revokes a device. The window
/// is short enough that a revocation is noticed in seconds — which is all it needs to be,
/// since the server refuses the revoked device on every other request regardless of what
/// this says.
/// </remarks>
public sealed class UpstreamContextProbe(HttpClient http, IUpstreamTarget target, Func<DateTime>? now = null)
    : IUpstreamContextProbe
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Lock _gate = new();
    private UpstreamContext? _cached;
    private DateTime _cachedAt;

    public async Task<UpstreamContext?> ReadAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_cached != null && _now() - _cachedAt < CacheLifetime)
            {
                return _cached;
            }
        }

        var fresh = await FetchAsync(ct);

        lock (_gate)
        {
            // A failure is not cached: the next call should try again rather than keep
            // reporting a server unreachable for the rest of the window.
            if (fresh != null)
            {
                _cached = fresh;
                _cachedAt = _now();
            }
        }

        return fresh;
    }

    private async Task<UpstreamContext?> FetchAsync(CancellationToken ct)
    {
        var destination = target.BaseAddress;

        if (string.IsNullOrEmpty(destination) ||
            !Uri.TryCreate(destination, UriKind.Absolute, out var root))
        {
            return null;
        }

        try
        {
            var response = await http.GetAsync(new Uri(root, ClientContextEndpoint.Path), ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = (await JsonSerializer.DeserializeAsync<Envelope<ContextPayload>>(
                await response.Content.ReadAsStreamAsync(ct), ServerJson.Options, ct))?.Data;

            return payload == null ? null : new UpstreamContext(payload.Mode, payload.Paired);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException &&
                                  !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed record Envelope<T>(int Code, string? Message, T? Data);

    private sealed record ContextPayload(bool IsLocal, RemoteAccessMode Mode, bool Paired);
}
