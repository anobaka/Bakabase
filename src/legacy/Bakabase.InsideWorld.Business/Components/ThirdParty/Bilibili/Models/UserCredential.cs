using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public record UserCredential
    {
        public TProfile Profile { get; set; }

        public record TProfile
        {
            public int Mid { get; set; }
        }
    }
}