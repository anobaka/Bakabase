using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    [Obsolete]
    public class Series
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = null!;
    }
}