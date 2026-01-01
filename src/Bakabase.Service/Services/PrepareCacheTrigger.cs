using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

public class PrepareCacheTrigger : IPrepareCacheTrigger, IDisposable
{
    private const string TaskId = "PrepareCache";
    private const int DebounceMs = 5000;

    private readonly BTaskManager _taskManager;
    private readonly ILogger<PrepareCacheTrigger> _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public PrepareCacheTrigger(BTaskManager taskManager, ILogger<PrepareCacheTrigger> logger)
    {
        _taskManager = taskManager;
        _logger = logger;
    }

    public void RequestTrigger()
    {
        if (_disposed) return;

        lock (_lock)
        {
            // Cancel previous debounce timer if exists
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogInformation("Triggering PrepareCache task after debounce window");
                        await _taskManager.Start(TaskId);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce was cancelled by a new request, this is expected
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to trigger PrepareCache task");
                }
            }, token);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }
}
