using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record ResourceDbModel
    {
        public int Id { get; set; }
        [Required] public string Path { get; set; } = null!;
        public bool IsFile { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;
        public DateTime UpdateDt { get; set; } = DateTime.Now;
        public DateTime FileCreateDt { get; set; }
        public DateTime FileModifyDt { get; set; }
        [Obsolete]
        public int MediaLibraryId { get; set; }
        [Obsolete]
        public int CategoryId { get; set; }
        public int? ParentId { get; set; }
        public ResourceTag Tags { get; set; }
        public DateTime? PlayedAt { get; set; }
    }
}