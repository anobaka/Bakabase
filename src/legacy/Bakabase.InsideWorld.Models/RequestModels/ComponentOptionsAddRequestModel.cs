using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    [Obsolete]
    public class ComponentOptionsAddRequestModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        [Required] public string ComponentAssemblyQualifiedTypeName { get; set; } = null!;
        [Required] public string Json { get; set; } = string.Empty;
    }
}
