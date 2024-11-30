using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationDbModel
    {
        [Key] public int Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        public string? Filter { get; set; }
        public string? Processes { get; set; }
        public string? Variables { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? FilteredResourceIds { get; set; }
        public DateTime? AppliedAt { get; set; }
    }
}