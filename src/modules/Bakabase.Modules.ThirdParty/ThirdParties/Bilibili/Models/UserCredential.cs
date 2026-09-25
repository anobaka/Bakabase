using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    /// <summary><c>x/space/v2/myinfo</c> data.</summary>
    public record UserCredential
    {
        public TProfile? Profile { get; set; }

        public record TProfile
        {
            /// <summary>
            /// The account id. <c>long</c>: newer accounts have 16-digit ids (e.g. 3546571234567890),
            /// which overflowed the former <c>int</c>.
            /// </summary>
            public long Mid { get; set; }
        }
    }
}
