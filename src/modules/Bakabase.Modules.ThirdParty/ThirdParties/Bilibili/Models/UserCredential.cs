using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
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