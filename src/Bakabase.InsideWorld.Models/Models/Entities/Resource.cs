using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
    public class Resource
    {
        public int Id { get; set; }
        [Obsolete]
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

        [Obsolete]
        public ResourceLanguage Language { get; set; } = ResourceLanguage.NotSet;
        [Obsolete]
        public decimal Rate { get; set; }
        public DateTime? ReleaseDt { get; set; }
        public string? Introduction { get; set; }

        #endregion

        [NotMapped]
        public string RawFullname
        {
            get
            {
                if (Directory.IsNullOrEmpty() && RawName.IsNullOrEmpty())
                {
                    return null;
                }

                return Path.Combine(new[] {Directory, RawName}.Where(a => a.IsNotEmpty()).ToArray()).StandardizePath();
            }
        }
    }
}