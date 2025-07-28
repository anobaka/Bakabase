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

        public ThirdPartyService(ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger, IThirdPartyStatisticsNotificationService? notificationService, ILogger<ThirdPartyService> logger)
        {
            _thirdPartyHttpRequestLogger = thirdPartyHttpRequestLogger;
            _notificationService = notificationService;
            _logger = logger;
            
            // Subscribe to request completion events
            _thirdPartyHttpRequestLogger.OnRequestCompleted += OnRequestCompleted;
        }

        public ThirdPartyRequestStatistics[] GetAllThirdPartyRequestStatistics()
        {
            var logs = _thirdPartyHttpRequestLogger.Logs;

            return logs?.Select(a => new ThirdPartyRequestStatistics
            {
                Id = a.Key,
                Counts = a.Value.GroupBy(b => b.Result).ToDictionary(x => (int)x.Key, x => x.Count())
            }).ToArray() ?? [];
        }
        
        private async void OnRequestCompleted(object? sender, ThirdPartyRequestCompletedEventArgs e)
        {
            try
            {
                if (_notificationService != null)
                {
                    var statistics = GetAllThirdPartyRequestStatistics();
                    await _notificationService.NotifyStatisticsChanged(statistics);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting third party request statistics");
            }
        }

        public void Dispose()
        {
            _thirdPartyHttpRequestLogger.OnRequestCompleted -= OnRequestCompleted;
        }
    }
}