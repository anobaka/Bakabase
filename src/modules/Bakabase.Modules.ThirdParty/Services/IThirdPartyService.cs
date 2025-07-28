using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Modules.ThirdParty.Services;

public interface IThirdPartyService
{
    ThirdPartyRequestStatistics[] GetAllThirdPartyRequestStatistics();
}