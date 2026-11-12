using System;
using System.Collections.Generic;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>An opaque local profile, scoped to one reviewed backend/service.</summary>
public sealed record RcloneProfileRef(string ProviderId, string Id);

/// <summary>Write-only allowlisted OAuth configuration; never accept an arbitrary rclone config.</summary>
public sealed class RcloneOneDriveConfiguration
{
    public string TokenJson { get; init; } = "";
    public string DriveId { get; init; } = "";
    public string DriveType { get; init; } = "personal";
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? AuthUrl { get; init; }
    public string? TokenUrl { get; init; }
    public string? RootFolderId { get; init; }
    public override string ToString() => "OneDrive configuration (credentials redacted)";
}

/// <summary>Account remote paths, not public sharing URLs. Hashes may be absent at the provider.</summary>
public sealed record RcloneRemoteFile(string Path, long Size, string? Id = null,
    IReadOnlyDictionary<string, string>? Hashes = null, string? ModifiedAt = null);
public sealed record RcloneCopyProgress(long Bytes, long TotalBytes, long CompletedFiles, long TotalFiles);
public sealed record RcloneLocalFile(string Path, long Size, string Sha256);
public sealed record RcloneCopyResult(IReadOnlyList<RcloneLocalFile> Files, bool Reused);

/// <summary>Only stable error codes cross the engine boundary; upstream text can contain tokens and signed URLs.</summary>
public sealed class RcloneRemoteException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
