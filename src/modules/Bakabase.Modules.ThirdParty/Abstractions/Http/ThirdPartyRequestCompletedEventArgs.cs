using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http;


public class ThirdPartyRequestCompletedEventArgs(ThirdPartyId thirdPartyId, ThirdPartyRequestResultType resultType)
    : EventArgs
{
    public ThirdPartyId ThirdPartyId { get; } = thirdPartyId;
    public ThirdPartyRequestResultType ResultType { get; } = resultType;
}