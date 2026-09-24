using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Components.Pairing;

public interface IRemoteDeviceService
{
    /// <summary>Whether any device has paired. Drives first-run behaviour.</summary>
    bool HasAnyDevice { get; }

    IReadOnlyList<RemoteDevice> GetDevices();

    /// <summary>Requests that have not expired and have not been collected yet.</summary>
    IReadOnlyList<PendingPairingRequest> GetPendingRequests();

    RemoteDevice? Find(string? deviceId);

    /// <summary>
    /// Issues a fresh code, invalidating any outstanding one. The plaintext is returned
    /// only here.
    /// </summary>
    Task<PairingCodeIssue> IssuePairingCodeAsync(TimeSpan? lifetime = null, CancellationToken ct = default);

    PairingCodeStatus? GetPairingCodeStatus();

    /// <summary>Exchanges a valid code for credentials. The code is consumed on success.</summary>
    Task<PairingResult> PairWithCodeAsync(string? code, string deviceName, RemoteDevicePlatform platform,
        CancellationToken ct = default);

    /// <summary>
    /// Asks an already-paired device to approve this one. Returns the request whose id
    /// the caller then polls.
    /// </summary>
    Task<PendingPairingRequest> RequestPairingAsync(string deviceName, RemoteDevicePlatform platform,
        string? remoteAddress, CancellationToken ct = default);

    /// <summary>
    /// Lets a waiting device in. Returns the id the device will have once it collects its
    /// key — the one it is listed under from then on — or null when there was nothing to
    /// approve (unknown, expired or already approved).
    /// </summary>
    Task<string?> ApproveRequestAsync(string requestId, string approvedByDeviceId, CancellationToken ct = default);

    Task<bool> RejectRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Collects the credentials an approval produced. Succeeds exactly once per request.
    /// </summary>
    Task<PairingResult> ClaimApprovedAsync(string requestId, CancellationToken ct = default);

    Task<bool> RevokeAsync(string deviceId, CancellationToken ct = default);

    Task<bool> RenameAsync(string deviceId, string name, CancellationToken ct = default);

    /// <summary>
    /// Records that a device is active. Writes at most once per
    /// <see cref="RemoteDevice.LastSeenPersistenceInterval"/>, so the hot path stays off
    /// the disk.
    /// </summary>
    Task TouchAsync(string deviceId, CancellationToken ct = default);
}

/// <summary>
/// Pairing: issuing codes, approving devices, and keeping the device list.
/// </summary>
/// <remarks>
/// <para>
/// Two ways in, because the server does not always have a screen. A container prints a
/// code to its log and the first device types it; after that, any paired device can
/// approve the next one, so nobody has to go back to the logs.
/// </para>
/// <para>
/// Everything lands in <see cref="IRemoteDeviceStore"/>, whose writes are serialized —
/// so a check-then-write (spend a code, claim an approval) happens inside one mutation
/// and two devices racing cannot both win.
/// </para>
/// </remarks>
public sealed class RemoteDeviceService(IRemoteDeviceStore store, Func<DateTime>? now = null) : IRemoteDeviceService
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    public bool HasAnyDevice => store.Read().Devices.Count > 0;

    public IReadOnlyList<RemoteDevice> GetDevices() => store.Read().Devices.ToArray();

    public IReadOnlyList<PendingPairingRequest> GetPendingRequests()
    {
        var now = _now();
        return store.Read().PendingRequests.Where(r => r.ExpiresAt > now && !r.IsApproved).ToArray();
    }

    public RemoteDevice? Find(string? deviceId) =>
        string.IsNullOrEmpty(deviceId)
            ? null
            : store.Read().Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

    public async Task<PairingCodeIssue> IssuePairingCodeAsync(TimeSpan? lifetime = null,
        CancellationToken ct = default)
    {
        var code = NewNumericCode(PairingCodeState.CodeDigits);
        var expiresAt = _now().Add(lifetime ?? PairingCodeState.DefaultLifetime);

        await store.MutateAsync(data =>
        {
            // Issuing replaces any outstanding code, so a user who reissues because
            // they lost the first one does not leave two valid codes in the wild.
            data.PairingCode = new PairingCodeState
            {
                CodeHash = HashCode(code),
                ExpiresAt = expiresAt,
                FailedAttempts = 0
            };
        }, ct);

        return new PairingCodeIssue(code, expiresAt);
    }

    public PairingCodeStatus? GetPairingCodeStatus()
    {
        var state = store.Read().PairingCode;
        if (state == null || state.ExpiresAt <= _now() || state.FailedAttempts >= PairingCodeState.MaxFailedAttempts)
        {
            return null;
        }

        return new PairingCodeStatus(state.ExpiresAt,
            PairingCodeState.MaxFailedAttempts - state.FailedAttempts);
    }

    public async Task<PairingResult> PairWithCodeAsync(string? code, string deviceName,
        RemoteDevicePlatform platform, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return PairingResult.Failed(PairingFailure.CodeRejected);
        }

        var now = _now();

        // Check and spend inside one mutation: two devices submitting the same code at
        // the same moment must not both get in.
        return await store.MutateAsync(data =>
        {
            var state = data.PairingCode;
            if (state == null || state.ExpiresAt <= now || state.FailedAttempts >= PairingCodeState.MaxFailedAttempts)
            {
                return PairingResult.Failed(PairingFailure.CodeRejected);
            }

            if (!FixedTimeEquals(state.CodeHash, HashCode(code.Trim())))
            {
                state.FailedAttempts++;
                return PairingResult.Failed(PairingFailure.CodeRejected);
            }

            // Consumed, whether or not anything else goes wrong from here.
            data.PairingCode = null;

            var device = NewDevice(deviceName, platform, now, approvedBy: null);
            data.Devices.Add(device);
            return PairingResult.Ok(new PairingCredentials(device.Id, device.Key));
        }, ct);
    }

    public async Task<PendingPairingRequest> RequestPairingAsync(string deviceName, RemoteDevicePlatform platform,
        string? remoteAddress, CancellationToken ct = default)
    {
        var now = _now();
        var request = new PendingPairingRequest
        {
            // Unguessable, because this id is the only thing the requesting device can
            // present when it comes back for its key.
            Id = RemoteRequestSignature.ToBase64Url(RandomNumberGenerator.GetBytes(16)),
            DeviceName = Sanitize(deviceName),
            Platform = platform,
            RemoteAddress = remoteAddress,
            RequestedAt = now,
            ExpiresAt = now.Add(PendingPairingRequest.DefaultLifetime)
        };

        await store.MutateAsync(data =>
        {
            DropExpired(data, now);
            TrimPending(data);
            data.PendingRequests.Add(request);
        }, ct);

        return request;
    }

    public async Task<string?> ApproveRequestAsync(string requestId, string approvedByDeviceId,
        CancellationToken ct = default)
    {
        var now = _now();

        return await store.MutateAsync<string?>(data =>
        {
            var request = data.PendingRequests.FirstOrDefault(r =>
                string.Equals(r.Id, requestId, StringComparison.Ordinal));

            if (request == null || request.ExpiresAt <= now || request.IsApproved)
            {
                return null;
            }

            // The device itself is not created yet — an approval nobody collects should
            // expire rather than leave a device in the list that can never authenticate.
            // Its id is decided now, so whoever approved it can find it once it is listed.
            var device = NewDevice(request.DeviceName, request.Platform, now, approvedByDeviceId);
            request.ApprovedDeviceId = device.Id;
            request.ApprovedKey = device.Key;
            request.ApprovedByDeviceId = approvedByDeviceId;
            return device.Id;
        }, ct);
    }

    public async Task<bool> RejectRequestAsync(string requestId, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
            data.PendingRequests.RemoveAll(r => string.Equals(r.Id, requestId, StringComparison.Ordinal)) > 0, ct);

    public async Task<PairingResult> ClaimApprovedAsync(string requestId, CancellationToken ct = default)
    {
        var now = _now();

        return await store.MutateAsync(data =>
        {
            var request = data.PendingRequests.FirstOrDefault(r =>
                string.Equals(r.Id, requestId, StringComparison.Ordinal));

            if (request == null || request.ExpiresAt <= now)
            {
                return PairingResult.Failed(PairingFailure.RequestRejected);
            }

            if (!request.IsApproved)
            {
                return PairingResult.Failed(PairingFailure.NotYetApproved);
            }

            var device = new RemoteDevice
            {
                Id = request.ApprovedDeviceId!,
                Name = request.DeviceName,
                Platform = request.Platform,
                Key = request.ApprovedKey!,
                CreatedAt = now,
                ApprovedByDeviceId = request.ApprovedByDeviceId
            };

            data.Devices.Add(device);
            data.PendingRequests.Remove(request);

            return PairingResult.Ok(new PairingCredentials(device.Id, device.Key));
        }, ct);
    }

    public async Task<bool> RevokeAsync(string deviceId, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
            data.Devices.RemoveAll(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal)) > 0, ct);

    public async Task<bool> RenameAsync(string deviceId, string name, CancellationToken ct = default) =>
        await store.MutateAsync(data =>
        {
            var device = data.Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));
            if (device == null)
            {
                return false;
            }

            device.Name = Sanitize(name);
            return true;
        }, ct);

    public async Task TouchAsync(string deviceId, CancellationToken ct = default)
    {
        var now = _now();
        var device = Find(deviceId);

        // Checked before the mutation so an active device does not serialize every one
        // of its requests behind the write gate.
        if (device == null ||
            (device.LastSeenAt.HasValue &&
             now - device.LastSeenAt.Value < RemoteDevice.LastSeenPersistenceInterval))
        {
            return;
        }

        await store.MutateAsync(data =>
        {
            var target = data.Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));
            if (target != null)
            {
                target.LastSeenAt = now;
            }
        }, ct);
    }

    private RemoteDevice NewDevice(string name, RemoteDevicePlatform platform, DateTime now, string? approvedBy) =>
        new()
        {
            Id = RemoteRequestSignature.ToBase64Url(RandomNumberGenerator.GetBytes(12)),
            Name = Sanitize(name),
            Platform = platform,
            Key = RemoteRequestSignature.ToBase64Url(RemoteRequestSignature.NewDeviceKey()),
            CreatedAt = now,
            ApprovedByDeviceId = approvedBy
        };

    private static void DropExpired(RemoteDeviceStoreData data, DateTime now) =>
        data.PendingRequests.RemoveAll(r => r.ExpiresAt <= now);

    /// <summary>
    /// Keeps room for the request about to be added. Unapproved requests go first: an
    /// approved one belongs to a device already waiting to collect its key, and dropping
    /// it would strand that device.
    /// </summary>
    private static void TrimPending(RemoteDeviceStoreData data)
    {
        while (data.PendingRequests.Count >= PendingPairingRequest.MaxPending)
        {
            var oldest = data.PendingRequests.Where(r => !r.IsApproved).MinBy(r => r.RequestedAt) ??
                         data.PendingRequests.MinBy(r => r.RequestedAt)!;
            data.PendingRequests.Remove(oldest);
        }
    }

    /// <summary>
    /// Digits only, so it can be read aloud and typed on a phone. Drawn from a uniform
    /// source rather than a seeded one — a guessable code is the whole attack.
    /// </summary>
    private static string NewNumericCode(int digits)
    {
        var sb = new StringBuilder(digits);
        for (var i = 0; i < digits; i++)
        {
            sb.Append((char) ('0' + RandomNumberGenerator.GetInt32(10)));
        }

        return sb.ToString();
    }

    private static string HashCode(string code) =>
        RemoteRequestSignature.ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>
    /// Device names are chosen by the device and shown in the approval prompt, so they
    /// are trimmed and bounded before anyone reads them.
    /// </summary>
    private static string Sanitize(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Unnamed device";
        }

        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }
}
