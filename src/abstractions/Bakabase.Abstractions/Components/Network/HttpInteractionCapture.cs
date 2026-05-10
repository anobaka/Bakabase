using System;
using System.Collections.Generic;
using System.Threading;

namespace Bakabase.Abstractions.Components.Network;

public record HttpInteractionRecord
{
    public string Method { get; init; } = null!;
    public string Url { get; init; } = null!;
    public Dictionary<string, string> RequestHeaders { get; init; } = new();
    public string? RequestBody { get; init; }
    public string? RequestContentType { get; init; }
    public int? ResponseStatusCode { get; init; }
    public string? ResponseReasonPhrase { get; init; }
    public Dictionary<string, string>? ResponseHeaders { get; init; }
    public string? ResponseContentType { get; init; }
    public long? ResponseContentLength { get; init; }
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}

/// <summary>
/// Activates per-async-flow capture of outgoing HTTP requests through HttpClients
/// that have <see cref="HttpInteractionCaptureHandler"/> in their pipeline.
/// Used by the AV test endpoint so users can inspect the actual request method,
/// URL, headers, and body that each scraper performed.
/// </summary>
public static class HttpInteractionCapture
{
    private static readonly AsyncLocal<List<HttpInteractionRecord>?> _current = new();

    public static List<HttpInteractionRecord>? Current => _current.Value;

    public static IDisposable Begin()
    {
        var bucket = new List<HttpInteractionRecord>();
        var previous = _current.Value;
        _current.Value = bucket;
        return new Scope(bucket, previous);
    }

    public static List<HttpInteractionRecord> EndAndCollect(IDisposable scope)
    {
        if (scope is Scope s)
        {
            s.Dispose();
            return s.Bucket;
        }
        return [];
    }

    private sealed class Scope(List<HttpInteractionRecord> bucket, List<HttpInteractionRecord>? previous) : IDisposable
    {
        public List<HttpInteractionRecord> Bucket { get; } = bucket;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
