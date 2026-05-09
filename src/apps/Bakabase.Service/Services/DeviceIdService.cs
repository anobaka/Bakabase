using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

/// <summary>
/// Anonymous, install-scoped device identifier shared across analytics SDKs (Clarity / GA4
/// / Sentry) so a single user can be cross-referenced between the three. Generated as a
/// UUID v4 on first launch and persisted as a single-line file under AppData. Carries no
/// PII and is not derived from hardware fingerprint.
/// </summary>
public interface IDeviceIdService
{
    /// <summary>Reads the persisted id, or generates and persists a new one.</summary>
    string GetOrCreate();

    /// <summary>Deletes the persisted id and returns a freshly generated one. Used by the
    /// "reset anonymous id" UX so users can break the linkage to their historical data.</summary>
    string Reset();
}

public class DeviceIdService(AppService appService, ILogger<DeviceIdService> logger) : IDeviceIdService
{
    private const string SubDir = "analytics";
    private const string FileName = "device-id";

    private readonly object _lock = new();
    private string? _cached;

    public string GetOrCreate()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;

            var dir = appService.RequestAppDataDirectory(SubDir);
            var path = Path.Combine(dir, FileName);

            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (Guid.TryParse(existing, out _))
                    {
                        _cached = existing;
                        return _cached;
                    }

                    logger.LogWarning("device-id file at {Path} is malformed; regenerating", path);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to read device-id at {Path}; regenerating", path);
                }
            }

            var id = Guid.NewGuid().ToString("D");
            try
            {
                File.WriteAllText(path, id);
            }
            catch (Exception e)
            {
                // We continue with an in-memory id so analytics can still init this run; the
                // next launch will retry persistence.
                logger.LogError(e, "Failed to persist device-id at {Path}; using ephemeral value", path);
            }

            _cached = id;
            return _cached;
        }
    }

    public string Reset()
    {
        lock (_lock)
        {
            var dir = appService.RequestAppDataDirectory(SubDir);
            var path = Path.Combine(dir, FileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete device-id at {Path}; new id will overwrite", path);
            }

            _cached = null;
            return GetOrCreate();
        }
    }
}
