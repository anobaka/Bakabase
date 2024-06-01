using Bakabase.Abstractions.Components.Tag;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db
{
    public record Tag: ITagBizKey
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = null!;
        public string? Group { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
    }
}