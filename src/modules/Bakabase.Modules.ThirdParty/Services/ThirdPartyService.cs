using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.Services
{
    public class ThirdPartyService : IThirdPartyService, IDisposable
    {
        private readonly ThirdPartyHttpRequestLogger _thirdPartyHttpRequestLogger;
        private readonly IThirdPartyStatisticsNotificationService? _notificationService;
        private readonly ILogger<ThirdPartyService> _logger;
        private readonly object _broadcastGate = new();
        private bool _broadcastPending;
        private bool _broadcastRunning;
        private bool _disposed;

        public ThirdPartyService(ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger, IThirdPartyStatisticsNotificationService? notificationService, ILogger<ThirdPartyService> logger)
        {
            _thirdPartyHttpRequestLogger = thirdPartyHttpRequestLogger;
            _notificationService = notificationService;
            _logger = logger;
            
            // Subscribe to request completion events
            _thirdPartyHttpRequestLogger.OnRequestCompleted += OnRequestCompleted;
            _thirdPartyHttpRequestLogger.OnTrafficChanged += OnTrafficChanged;
        }

        public ThirdPartyRequestStatistics[] GetAllThirdPartyRequestStatistics()
        {
            var logs = _thirdPartyHttpRequestLogger.Logs;

            return logs?.Select(a => new ThirdPartyRequestStatistics
            {
                Id = a.Key,
                Counts = a.Value.GroupBy(b => b.Result).ToDictionary(x => (int)x.Key, x => x.Count()),
                ReceivedBytes = a.Value.Sum(b => b.ReceivedBytes)
            }).ToArray() ?? [];
        }

        private void OnTrafficChanged(object? sender, EventArgs e) => QueueBroadcast();
        
        private void OnRequestCompleted(object? sender, ThirdPartyRequestCompletedEventArgs e) => QueueBroadcast();

        private void QueueBroadcast()
        {
            if (_notificationService == null) return;

            lock (_broadcastGate)
            {
                if (_disposed) return;
                _broadcastPending = true;
                if (_broadcastRunning) return;
                _broadcastRunning = true;
            }

            _ = BroadcastStatisticsAsync();
        }

        private async Task BroadcastStatisticsAsync()
        {
            while (true)
            {
                lock (_broadcastGate)
                {
                    if (_disposed || !_broadcastPending)
                    {
                        _broadcastRunning = false;
                        return;
                    }

                    // All events received during a slow send become one fresh snapshot.
                    _broadcastPending = false;
                }

                try
                {
                    await _notificationService!.NotifyStatisticsChanged(GetAllThirdPartyRequestStatistics());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting third party request statistics");
                }
            }
        }

        public void Dispose()
        {
            lock (_broadcastGate)
            {
                _disposed = true;
                _broadcastPending = false;
            }

            _thirdPartyHttpRequestLogger.OnRequestCompleted -= OnRequestCompleted;
            _thirdPartyHttpRequestLogger.OnTrafficChanged -= OnTrafficChanged;
        }
    }
}
