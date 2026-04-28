using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

public record BulkModificationProcessInputModel
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public string? Steps { get; set; }
}