using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Http;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Logging;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ThirdPartyService
    {
        private readonly ThirdPartyHttpRequestLogger _thirdPartyHttpRequestLogger;

        public ThirdPartyService(ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger)
        {
            _thirdPartyHttpRequestLogger = thirdPartyHttpRequestLogger;
        }

        public ThirdPartyRequestStatistics[] GetAllThirdPartyRequestStatistics()
        {
            var logs = _thirdPartyHttpRequestLogger.Logs;

            return logs?.Select(a => new ThirdPartyRequestStatistics
                {
                    Id = a.Key, Counts = a.Value.GroupBy(b => b.Result).ToDictionary(x => (int) x.Key, x => x.Count())
                })
                .ToArray() ?? Array.Empty<ThirdPartyRequestStatistics>();
        }
    }
}