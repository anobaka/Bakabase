using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Modules.ThirdParty.Services;

public interface IThirdPartyStatisticsNotificationService
{
    Task NotifyStatisticsChanged(ThirdPartyRequestStatistics[] statistics);
}