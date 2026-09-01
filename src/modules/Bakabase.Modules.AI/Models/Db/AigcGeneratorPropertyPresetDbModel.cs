using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.AI.Models.Db;

/// <summary>
/// A fixed property value applied to every Resource produced by a generator.
/// Stored as a serialized BizValue using StandardValueSystem.Serialize.
/// </summary>
public record AigcGeneratorPropertyPresetDbModel
{
    [Key]
    public int Id { get; set; }

    public int GeneratorId { get; set; }
    public PropertyPool Pool { get; set; }
    public int PropertyId { get; set; }

    /// <summary>
    /// Serialized BizValue produced by StandardValueSystem.Serialize.
    /// Reverse via StandardValueSystem.Deserialize using the property's BizValueType.
    /// </summary>
    public string? SerializedBizValue { get; set; }
}
