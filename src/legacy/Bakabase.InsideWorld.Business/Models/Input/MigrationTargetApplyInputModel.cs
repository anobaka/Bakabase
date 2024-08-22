using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Models.Input
{
    public record MigrationTargetApplyInputModel
    {
        public ResourceProperty Property { get; set; }
        public string? PropertyKey { get; set; }
        public int? TargetPropertyId { get; set; }
    }
}
