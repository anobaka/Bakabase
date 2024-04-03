using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Models.Db
{
    public record Resource
    {
        public int Id { get; set; }
        public DateTime CreateDt { get; set; } = DateTime.Now;
        public DateTime UpdateDt { get; set; } = DateTime.Now;
        public DateTime FileCreateDt { get; set; }
        public DateTime FileModifyDt { get; set; }
        public int MediaLibraryId { get; set; }
        public int CategoryId { get; set; }
        public int? ParentId { get; set; }
        public bool HasChildren { get; set; }

        #region Properties

        [Obsolete] public string? Name { get; set; }
        [Obsolete] public ResourceLanguage Language { get; set; } = ResourceLanguage.NotSet;
        [Obsolete] public decimal Rate { get; set; }
        [Obsolete] public DateTime? ReleaseDt { get; set; }
        [Obsolete] public string? Introduction { get; set; }
        [Obsolete] [Required] public string RawName { get; set; } = null!;
        [Obsolete] [Required] public string Directory { get; set; } = null!;
        [Obsolete] public bool IsSingleFile { get; set; }

        #endregion
    }
}