using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public record ResourceCategory
    {
        public int Id { get; set; }
        [Required]
        public string Name { get; set; }
        [Obsolete]
        public string? ComponentsJsonData { get; set; }
        public string? Color { get; set; }
        public int Order { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;
        public CoverSelectOrder CoverSelectionOrder { get; set; }
        [Obsolete]
        public bool TryCompressedFilesOnCoverSelection { get; set; }
        public string? EnhancementOptionsJson { get; set; }
        public bool GenerateNfo { get; set; }

        public ResourceCategory Duplicate(string name)
        {
            return this with
            {
                Name = name,
                Id = 0,
                CreateDt = DateTime.Now
            };
        }
    }
}