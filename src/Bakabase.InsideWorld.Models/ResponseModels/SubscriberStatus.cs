using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public class SubscriberStatus
    {
        public bool Subscribing { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, int> Harvest { get; set; }
    }
}
