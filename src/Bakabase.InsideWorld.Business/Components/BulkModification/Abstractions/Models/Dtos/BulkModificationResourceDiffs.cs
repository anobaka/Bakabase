using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos
{
    public record BulkModificationResourceDiffs
    {
        public int Id { get; set; }
        public string Path { get; set; } = null!;
        public List<Diff>? Diffs { get; set; }

        public record Diff
        {
            public BulkModificationProperty Property { get; set; }
            public string? PropertyKey { get; set; }
            public BulkModificationDiffType Type { get; set; }
            public string? CurrentValue { get; set; }
            public string? NewValue { get; set; }
            public BulkModificationDiffOperation Operation { get; set; }
        }
    }
}
