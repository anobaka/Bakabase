using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

public record ResourceSearchFilterInputModel
{
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public SearchOperation? Operation { get; set; }
    /// <summary>
    /// Serialized
    /// </summary>
    public string? DbValue { get; set; }
}