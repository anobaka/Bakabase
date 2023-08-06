using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Subscribers
{
    public abstract class AbstractJsonSubscriber<TBody, TPost> : AbstractSimpleHttpSubscriber<TPost>
    {
        protected AbstractJsonSubscriber(IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(
            serviceProvider, loggerFactory)
        {
        }

        protected override List<TPost> ParseList(string body)
        {
            var b = JsonConvert.DeserializeObject<TBody>(body);
            return ParseListInterval(b);
        }

        protected abstract List<TPost> ParseListInterval(TBody body);
    }
}