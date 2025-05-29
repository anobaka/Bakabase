using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record CategoryDbModel
    {
        public int Id { get; set; }
        [Required] public string Name { get; set; } = null!;
        [Obsolete] public string? ComponentsJsonData { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;
        public CoverSelectOrder CoverSelectionOrder { get; set; }
        [Obsolete] public bool TryCompressedFilesOnCoverSelection { get; set; }
        public string? EnhancementOptionsJson { get; set; }
        public bool GenerateNfo { get; set; }
        public string? ResourceDisplayNameTemplate { get; set; }
    }
}