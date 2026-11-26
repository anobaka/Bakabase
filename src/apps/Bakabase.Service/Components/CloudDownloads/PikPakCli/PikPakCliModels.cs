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
    public bool CanManageFiles { get; init; }
    public bool CanPermanentlyDeleteFiles { get; init; }
    public bool PermanentlyDeleteTemporaryCopiesAfterDownload { get; init; }
    public bool TrashTemporaryCopiesAfterDownload { get; init; }
}

public sealed record PikPakCliCleanupSettingsInputModel(string ExpectedAccountId, string ExpectedAccountVersion,
    bool TrashTemporaryCopiesAfterDownload, bool PermanentlyDeleteTemporaryCopiesAfterDownload = false);

public sealed record PikPakCliQuotaViewModel(long? UsedBytes, long? LimitBytes, long? RemainingTransferBytes);

public sealed record PikPakCliCapacityRangeViewModel(decimal Minimum, decimal Maximum);

public sealed record PikPakCliCapacityViewModel
{
    public string? AccountId { get; init; }
    public string? AccountVersion { get; init; }
    public string Status { get; init; } = "unavailable";
    public string? Problem { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTimeOffset? CheckedAt { get; init; }
    public PikPakCliCapacityRangeViewModel? UsedBytes { get; init; }
    public PikPakCliCapacityRangeViewModel? TotalBytes { get; init; }
    public PikPakCliCapacityRangeViewModel? TrashBytes { get; init; }
    public PikPakCliCapacityRangeViewModel? AvailableBytes { get; init; }
    public bool Unlimited { get; init; }
}

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
    internal bool CanWaitForCapacity { get; init; }
}

// Diagnostic facts, never a promise that a remote write was rejected. In particular,
// neither a CLI exit code nor a quota error proves the server did not accept a save.
internal sealed record PikPakCliFailureEvidence(string Phase, int? ExitCode, string ResponseKind, string? ProviderCode);
