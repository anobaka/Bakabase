using System;
using System.Collections.Generic;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Subscribers
{
    public abstract class AbstractHtmlSubscriber : AbstractSimpleHttpSubscriber<CQ>
    {
        protected AbstractHtmlSubscriber(IServiceProvider serviceProvider, ILoggerFactory loggerFactory) : base(
            serviceProvider, loggerFactory)
        {
        }

        protected override List<CQ> ParseList(string body)
        {
            return ParseListInternal(new CQ(body));
        }

        protected abstract List<CQ> ParseListInternal(CQ cq);
    }
}