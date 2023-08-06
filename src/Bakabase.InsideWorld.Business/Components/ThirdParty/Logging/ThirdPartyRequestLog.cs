using System;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Logging
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