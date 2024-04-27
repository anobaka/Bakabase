using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Models.Domain
{
    public record MigrationTarget
    {
        public int DataCount { get; set; }
        public ResourceProperty? Property { get; set; }
        public string? PropertyKey { get; set; }
        public int? TargetPropertyId { get; set; }
        public IEnumerable<MigrationTarget>? SubTargets { get; set; }
        public object? Data { get; set; }
        public List<string>? DataForDisplay { get; set; }
    }
}