using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Services;

public interface IThirdPartyService
{
    ThirdPartyRequestStatistics[] GetAllThirdPartyRequestStatistics();
}