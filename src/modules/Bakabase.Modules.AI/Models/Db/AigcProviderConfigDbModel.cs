using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.AI.Models.Domain;

namespace Bakabase.Modules.AI.Models.Db;

public record AigcProviderConfigDbModel
{
    [Key]
    public int Id { get; set; }
    public AigcProviderKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Kind-specific configuration as JSON. Schema is defined per-kind:
    /// - HttpCustom: request/response template
    /// - ComfyUI: default workflow JSON
    /// - SD WebUI / OpenAI / Gemini: optional defaults (model/sampler/etc.)
    /// </summary>
    public string? ConfigJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
