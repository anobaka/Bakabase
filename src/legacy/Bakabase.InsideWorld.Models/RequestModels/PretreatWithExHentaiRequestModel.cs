using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class PretreatWithExHentaiRequestModel
    {
        public string Cookie { get; set; }
        public string RootPath { get; set; }
        public Dictionary<string, string> ManualSelection { get; set; } = new();
    }
}
