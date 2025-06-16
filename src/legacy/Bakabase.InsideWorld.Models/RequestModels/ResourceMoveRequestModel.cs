using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class ResourceMoveRequestModel
    {
        [Required] [BindRequired] public int[] Ids { get; set; } = null!;
        public int MediaLibraryId { get; set; }
        public bool IsLegacyMediaLibrary { get; set; }
        public string? Path { get; set; }
    }
}