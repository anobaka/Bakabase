using System;
using System.Collections.Generic;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

public sealed record RcloneAccountViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProviderId { get; init; }
    public bool IsConfigured { get; init; }
    public bool IsDefault { get; init; }
    public DateTimeOffset? LastValidatedAt { get; init; }
    public string? Problem { get; init; }
}

/// <summary>Secrets are accepted only on writes and never returned by account/session views.</summary>
public sealed record RcloneAccountInputModel
{
    public string? Id { get; init; }
    public required string ProviderId { get; init; }
    public required string Name { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? RootFolderId { get; init; }
    public string? ApiKey { get; init; }
    public override string ToString() => "Rclone account input (credentials redacted)";
}

public sealed record RcloneConfigurationExampleViewModel(string Value, string Help);
public sealed record RcloneConfigurationQuestionViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Help { get; init; }
    public required string Type { get; init; }
    public bool Required { get; init; }
    public bool IsSecret { get; init; }
    public bool Exclusive { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<RcloneConfigurationExampleViewModel> Examples { get; init; } = [];
}

public sealed record RcloneAuthorizationSessionViewModel
{
    public required string Id { get; init; }
    public required string AccountId { get; init; }
    public required string Status { get; init; }
    public string AuthorizationMode { get; init; } = "manual";
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTimeOffset ExpiresAt { get; init; }
    public RcloneConfigurationQuestionViewModel? Question { get; init; }
    public string? RemoteAuthorizeCommand { get; init; }
    public string? Problem { get; init; }
    public RcloneBrowserAuthorizationViewModel? Browser { get; init; }
}

public sealed record RcloneAuthorizationAnswerInputModel
{
    public required string QuestionId { get; init; }
    public required string Value { get; init; }
    public override string ToString() => "Rclone authorization answer (redacted)";
}

public sealed record RcloneBrowserAuthorizationViewModel
{
    public required string Status { get; init; }
    public string? AuthorizationUrl { get; init; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTimeOffset ExpiresAt { get; init; }
    public string? Problem { get; init; }
}

public sealed record RcloneBrowserAuthorizationStartInputModel
{
    public required string QuestionId { get; init; }
}

public sealed record RcloneBrowserAuthorizationCallbackInputModel
{
    public required string QuestionId { get; init; }
    public required string CallbackUrl { get; init; }
    public override string ToString() => "Rclone browser callback (redacted)";
}

public sealed record RcloneDriveCandidateViewModel
{
    public required string DriveId { get; init; }
    public required string Name { get; init; }
    public string? DriveType { get; init; }
    public bool IsPrimary { get; init; }
    public string AccessStatus { get; init; } = "unchecked";
    public string? Problem { get; init; }
}

public sealed record RcloneDriveChoicesViewModel
{
    public required string QuestionId { get; init; }
    public IReadOnlyList<RcloneDriveCandidateViewModel> Candidates { get; init; } = [];
    public string? Problem { get; init; }
}

public sealed record RcloneDrivePreviewInputModel
{
    public required string QuestionId { get; init; }
    public required string DriveId { get; init; }
}

public sealed record RcloneDrivePreviewItemViewModel(string Name, bool IsDirectory, long? Size);

public sealed record RcloneDrivePreviewViewModel
{
    public required string QuestionId { get; init; }
    public required string DriveId { get; init; }
    public string? Name { get; init; }
    public string? DriveType { get; init; }
    public IReadOnlyList<RcloneDrivePreviewItemViewModel> Items { get; init; } = [];
    public bool HasMore { get; init; }
}
