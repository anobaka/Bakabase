using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;

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
        public int MediaLibraryId { get; set; }
        public int CategoryId { get; set; }
        public int? ParentId { get; set; }
        public ResourceTag Tags { get; set; }
    }
}