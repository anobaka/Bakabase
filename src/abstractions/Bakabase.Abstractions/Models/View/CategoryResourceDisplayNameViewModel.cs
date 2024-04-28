using Bakabase.Abstractions.Models.View.Constants;

namespace Bakabase.Abstractions.Models.View;

public record CategoryResourceDisplayNameViewModel()
{
    public int ResourceId { get; set; }
    public string ResourcePath { get; set; } = null!;
    public Segment[] Segments { get; set; } = [];

    public record Segment
    {
        public CategoryResourceDisplayNameSegmentType Type { get; set; }
        public string Text { get; set; } = null!;
        public string? WrapperPairId { get; set; }
    }
}