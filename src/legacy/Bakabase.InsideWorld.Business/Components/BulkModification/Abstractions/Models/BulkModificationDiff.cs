using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationDiff
    {
        public int Id { get; set; }
        public int BulkModificationId { get; set; }
        /// <summary>
        /// redundancy
        /// </summary>
        public string ResourcePath { get; set; } = null!;
        public int ResourceId { get; set; }
        public BulkModificationFilterableProperty Property { get; set; }
        public string? PropertyKey { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? CurrentValue { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? NewValue { get; set; }

        public BulkModificationDiffType Type { get; set; }
        public BulkModificationDiffOperation Operation { get; set; } = BulkModificationDiffOperation.None;
    }
}