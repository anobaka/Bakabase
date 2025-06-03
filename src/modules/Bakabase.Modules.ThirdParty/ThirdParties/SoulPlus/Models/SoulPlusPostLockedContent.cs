using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models
{
    public record SoulPlusPostLockedContent
    {
        public int Id { get; set; }
        public string Content { get; set; } = null!;
        public int Price { get; set; }
        public bool IsBought { get; set; }
    }
}
