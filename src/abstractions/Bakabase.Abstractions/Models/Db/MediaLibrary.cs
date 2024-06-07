using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db
{
    public record MediaLibrary
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = string.Empty;
        [Required] public int CategoryId { get; set; }
        public string? PathConfigurationsJson { get; set; }
        public int Order { get; set; }
        [Obsolete] public int ResourceCount { get; set; }
    }
}