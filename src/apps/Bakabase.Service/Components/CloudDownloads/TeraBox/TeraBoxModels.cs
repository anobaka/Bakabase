using System;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

public sealed record TeraBoxAccountViewModel
{
    public bool IsConfigured { get; init; }
    public string? AccountId { get; init; }
    public string? AccountVersion { get; init; }
    public string? DisplayName { get; init; }
    public string? Problem { get; init; }
}

public sealed record TeraBoxAuthorizationViewModel
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? AuthorizationUri { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTimeOffset ExpiresAt { get; init; }
    public string? Problem { get; init; }
}

public sealed record TeraBoxAuthorizationCompleteInput { public string Code { get; init; } = ""; }
public sealed class TeraBoxException(string code) : InvalidOperationException(code)
{ public string Code { get; } = code; }
