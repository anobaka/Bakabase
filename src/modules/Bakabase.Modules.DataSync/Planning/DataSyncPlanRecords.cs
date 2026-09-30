namespace Bakabase.Modules.DataSync.Planning;

// Display values and warnings, exposed over HTTP: Newtonsoft-safe (no JsonNode, no object, no enum-keyed
// dictionaries).

public sealed record DataSyncDisplayValue(
    string? Text, string? Color = null, string? Group = null, IReadOnlyList<string>? Path = null,
    bool? Flag = null, int? Number = null);

/// <summary>ChangeId ties a warning to one change, when it has one.</summary>
public sealed record DataSyncPlanWarning(DataSyncWarningCode Code, string? ChangeId,
    IReadOnlyDictionary<string, string>? Args);
