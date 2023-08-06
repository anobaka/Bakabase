using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class UserCredential
    {
        [JsonProperty("confirm_url")]
        public string ConfirmUrl { get; set; }
        [JsonProperty("user_info")]
        public TUserInfo UserInfo { get; set; }

        public class TUserInfo
        {
            public int Mid { get; set; }
        }
    }
}