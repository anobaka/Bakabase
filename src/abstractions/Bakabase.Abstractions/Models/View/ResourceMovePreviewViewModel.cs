using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.View;

/// <summary>
/// What a move would do, computed before any file is touched: the effective top-level
/// resources with their destination paths, per-resource conflict flags, and the
/// property/media-library marks expected to cover the destination.
/// </summary>
public record ResourceMovePreviewViewModel
{
    public List<Item> Items { get; set; } = [];

    public record Item
    {
        public int ResourceId { get; set; }
        public string SourcePath { get; set; } = null!;
        public string DestPath { get; set; } = null!;

        /// <summary>The destination path is already occupied — this resource's move will fail.</summary>
        public bool DestConflict { get; set; }

        /// <summary>The destination sits inside this resource's own subtree — the batch will be rejected.</summary>
        public bool DestInsideSource { get; set; }

        public List<MarkEffect> Effects { get; set; } = [];
    }

    public record MarkEffect
    {
        public int MarkId { get; set; }
        public PathMarkType Type { get; set; }
        public string MarkPath { get; set; } = null!;

        /// <summary>Best-effort evaluation of the mark's match config against the destination path.</summary>
        public bool WillApply { get; set; }

        /// <summary>Fixed-target property mark: the property's display name.</summary>
        public string? PropertyName { get; set; }

        /// <summary>Fixed-value property mark: display text of the value.</summary>
        public string? FixedValue { get; set; }

        /// <summary>The mark derives its value/target from the path (Dynamic mode).</summary>
        public bool IsDynamic { get; set; }

        /// <summary>Fixed-target media-library mark: the library's name.</summary>
        public string? MediaLibraryName { get; set; }
    }
}
