using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.AI.Models.Db;

public record LlmToolConfigDbModel
{
    [Key]
    public int Id { get; set; }
    /// <summary>
    /// The tool function name (matches AIFunction.Name / LlmToolMetadata.Name).
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
