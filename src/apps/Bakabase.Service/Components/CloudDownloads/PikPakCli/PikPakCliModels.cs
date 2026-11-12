using System;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed record PikPakCliAccountViewModel
{
    public bool IsConfigured { get; init; }
    public string? AccountId { get; init; }
    public string? AccountVersion { get; init; }
    public string? DisplayName { get; init; }
    public string? Problem { get; init; }
    public PikPakCliQuotaViewModel? Quota { get; init; }
}

public sealed record PikPakCliQuotaViewModel(long? UsedBytes, long? LimitBytes, long? RemainingTransferBytes);

public sealed record PikPakCliAuthorizationViewModel
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public string? VerificationUri { get; init; }
    public string? UserCode { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTimeOffset ExpiresAt { get; init; }
    public int IntervalSeconds { get; init; } = 3;
    public string? Problem { get; init; }
}

public sealed class PikPakCliException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    internal PikPakCliFailureEvidence? Evidence { get; init; }
}

// Diagnostic facts, never a promise that a remote write was rejected. In particular,
// neither a CLI exit code nor a quota error proves the server did not accept a save.
internal sealed record PikPakCliFailureEvidence(string Phase, int? ExitCode, string ResponseKind, string? ProviderCode);
