using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus.Models
{
    public record SoulPlusPostLockedContent
    {
        // public int Id { get; set; }
        public string? Url { get; set; }
        public int? Price { get; set; }
        public string? ContentHtml { get; set; }
        public bool IsBought { get; set; }
    }
}