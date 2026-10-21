using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

/// <summary>
/// Anonymous deployment identifier, retained for compatibility across upgrades.
/// The v2 client identity is separate; this ID counts servers, not users. Generated
/// randomly and persisted under effective AppData, never derived from a hardware fingerprint.
/// </summary>
public interface IDeviceIdService
{
    /// <summary>Reads a durable id, or creates one. Empty means identity is unavailable and telemetry must stop.</summary>
    string GetOrCreate();
}

public class DeviceIdService : IDeviceIdService
{
    private const string SubDir = "analytics";
    private const string FileName = "device-id";

    private readonly object _lock = new();
    private readonly Func<string> _directory;
    private readonly ILogger<DeviceIdService> _logger;
    private string? _cached;

    public DeviceIdService(AppService appService, ILogger<DeviceIdService> logger)
        : this(() => appService.RequestAppDataDirectory(SubDir), logger) { }

    internal DeviceIdService(Func<string> directory, ILogger<DeviceIdService> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public string GetOrCreate()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;

            // Cache failure for this process too: never manufacture a second deployment identity.
            _cached = string.Empty;
            try
            {
                var path = Path.Combine(_directory(), FileName);
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (Guid.TryParseExact(existing, "D", out var parsed) && parsed != Guid.Empty)
                    {
                        _cached = existing;
                    }
                    else _logger.LogWarning("Deployment analytics identity is malformed; telemetry is disabled");
                    return _cached;
                }
                var id = Guid.NewGuid().ToString("D");
                // CreateNew prevents accidental replacement of an identity created concurrently.
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(file))
                {
                    writer.Write(id);
                    writer.Flush();
                    file.Flush(flushToDisk: true);
                }
                _cached = id;
            }
            catch (Exception)
            {
                // No exception/path content is sent to diagnostics, and startup remains unaffected.
                _logger.LogWarning("Deployment analytics identity is unavailable; telemetry is disabled");
            }
            return _cached;
        }
    }
}
