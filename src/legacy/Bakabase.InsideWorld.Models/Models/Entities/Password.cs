using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class Password
    {
        [Key] [MaxLength(64)] public string Text { get; set; } = null!;
        public int UsedTimes { get; set; }
        public DateTime LastUsedAt { get; set; }
    }
}