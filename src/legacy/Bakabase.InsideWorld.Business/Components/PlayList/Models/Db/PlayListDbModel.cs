using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.PlayList.Models.Db
{
    public class PlayListDbModel
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; } = null!;
        public string? ItemsJson { get; set; }
        public int Interval { get; set; } = 3000;
        public int Order { get; set; }
    }
}