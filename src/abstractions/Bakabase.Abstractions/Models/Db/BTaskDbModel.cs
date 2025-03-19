using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Models.Db
{
    public record BTaskDbModel
    {
        public string Key { get; set; } = null!;
        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
        public DateTime? EnableAfter { get; set; }
    }
}