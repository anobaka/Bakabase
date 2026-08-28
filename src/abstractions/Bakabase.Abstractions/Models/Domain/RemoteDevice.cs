using System;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// A client device that has been paired for remote (non-loopback) access.
/// The token itself is never stored — only its hash — so a leaked options file
/// does not hand out working credentials.
/// </summary>
public class RemoteDevice
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public RemoteDevicePlatform Platform { get; set; }

    /// <summary>
    /// Base64 of SHA-256 over the device token. Compared in constant time.
    /// </summary>
    public string TokenHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last time this device made an authenticated request. Persisted lazily
    /// (at most once every <see cref="LastSeenPersistenceInterval"/>) so ordinary
    /// browsing does not rewrite the options file on every request.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    public static readonly TimeSpan LastSeenPersistenceInterval = TimeSpan.FromMinutes(10);
}
