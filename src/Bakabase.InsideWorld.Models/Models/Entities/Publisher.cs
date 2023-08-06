using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class Publisher
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        public int Rank { get; set; } = 0;
        public bool Favorite { get; set; }
    }
}