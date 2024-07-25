using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.ThirdParty.Abstractions.Logging
{
    public class ThirdPartyRequestLog
    {
        public string Key { get; set; }
        public ThirdPartyId ThirdPartyId { get; set; }
        public DateTime RequestTime { get; set; }
        public long ElapsedMs { get; set; }
        public ThirdPartyRequestResultType Result { get; set; }
        public string Message { get; set; }
    }
}