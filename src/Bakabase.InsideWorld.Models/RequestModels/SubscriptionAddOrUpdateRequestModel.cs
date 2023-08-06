using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class SubscriptionAddOrUpdateRequestModel
    {
        public Dictionary<SubscriptionType, DateTime?> TypeAndStartDts { get; set; }
        public string Keyword { get; set; }
    }
}
