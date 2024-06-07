using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Alias.Models.Input
{
    public class AliasAddInputModel
    {
        [Required]
        public string Text { get; set; } = string.Empty;
        public string? Preferred { get; set; }
    }
}
