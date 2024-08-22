using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Models
{
    [Obsolete]
    public class LegacyDbResource
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;
        public DateTime UpdateDt { get; set; } = DateTime.Now;
        public DateTime FileCreateDt { get; set; }
        public DateTime FileModifyDt { get; set; }

        public bool IsSingleFile { get; set; }

        // /// <summary>
        // /// Not filesystem related.
        // /// </summary>
        // public string Fullname { get; set; }
        public int MediaLibraryId { get; set; }
        public int CategoryId { get; set; }
        [Required] public string RawName { get; set; } = null!;
        [Required]
        public string Directory { get; set; } = null!;
        public int? ParentId { get; set; }
        public bool HasChildren { get; set; }

        #region Properties

        public ResourceLanguage Language { get; set; } = ResourceLanguage.NotSet;
        public decimal Rate { get; set; }
        public DateTime? ReleaseDt { get; set; }
        public string? Introduction { get; set; }

        #endregion

        [NotMapped]
        public string RawFullname
        {
            get
            {
                if (string.IsNullOrEmpty(Directory) && string.IsNullOrEmpty(RawName))
                {
                    return null;
                }
        
                return Path.Combine(new[] {Directory, RawName}.Where(a => !string.IsNullOrEmpty(a)).ToArray()).StandardizePath()!;
            }
        }
    }
}