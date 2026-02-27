using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db
{
    public record ResourceDbModel
    {
        public int Id { get; set; }
        public string? Path { get; set; }
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
        public ResourceSource Source { get; set; } = ResourceSource.FileSystem;
        public ResourceStatus Status { get; set; } = ResourceStatus.Active;
        [Required] public string SourceKey { get; set; } = null!;
    }
}