using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Services;

/// <summary>
/// Shared on-demand scan for the current effective profile. The last completed result stays
/// cached until an explicit refresh or data-directory change; a cancelled HTTP reader does
/// not cancel the scan other windows are awaiting.
/// </summary>
public sealed class AppStorageUsageService
{
    private readonly Func<string> _dataDirectory;
    private readonly CancellationToken _stoppingToken;
    private readonly Func<string, CancellationToken, AppStorageUsageViewModel> _measure;
    private readonly object _gate = new();
    private AppStorageUsageViewModel? _cached;
    private Task<AppStorageUsageViewModel>? _scan;
    private string? _scanRoot;

    public AppStorageUsageService(Func<string> dataDirectory, CancellationToken stoppingToken)
        : this(dataDirectory, stoppingToken, AppStorageUsageScanner.Scan) { }

    internal AppStorageUsageService(Func<string> dataDirectory, CancellationToken stoppingToken,
        Func<string, CancellationToken, AppStorageUsageViewModel> measure)
    {
        _dataDirectory = dataDirectory;
        _stoppingToken = stoppingToken;
        _measure = measure;
    }

    public async Task<AppStorageUsageViewModel> GetAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_dataDirectory()));
        Task<AppStorageUsageViewModel> scan;
        lock (_gate)
        {
            if (!forceRefresh && _cached?.RootPath == root) return _cached with {FromCache = true};
            if (_scan is {IsCompleted: false} && _scanRoot == root) scan = _scan;
            else
            {
                _scanRoot = root;
                _scan = scan = Task.Run(() =>
                {
                    var result = _measure(root, _stoppingToken);
                    lock (_gate) { if (_scanRoot == root) _cached = result; }
                    return result;
                }, _stoppingToken);
            }
        }
        return await scan.WaitAsync(ct);
    }
}
