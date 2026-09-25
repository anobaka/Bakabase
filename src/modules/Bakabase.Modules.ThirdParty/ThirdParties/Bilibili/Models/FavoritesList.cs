using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class FavoritesList
    {
        /// <summary>Null when the account has no folders.</summary>
        public List<ApiFavorites>? List { get; set; }
    }
}
