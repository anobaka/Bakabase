using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Models.View;

public record CustomPropertyTypeConversionPreviewViewModel
{
    public int DataCount { get; set; }
    public List<Change> Changes { get; set; } = [];
    public StandardValueType FromType { get; set; }
    public StandardValueType ToType { get; set; }

    public record Change(string? SerializedFromValue, string? SerializedToValue);
}