using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public class SubscriptionSyncStatus
    {
        public bool Syncing => Status.Any(a => a.Value.Subscribing);

        public Dictionary<SubscriptionType, SubscriberStatus> Status { get; set; } =
            new Dictionary<SubscriptionType, SubscriberStatus>();
    }
}