using System;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.AI.Models.Db;

public record ChatMessageDbModel
{
    [Key]
    public long Id { get; set; }
    public int ConversationId { get; set; }
    /// <summary>
    /// system, user, assistant, tool
    /// </summary>
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    /// <summary>
    /// JSON-serialized tool call requests from the assistant
    /// </summary>
    public string? ToolCalls { get; set; }
    /// <summary>
    /// JSON-serialized tool call results
    /// </summary>
    public string? ToolResults { get; set; }
    /// <summary>
    /// JSON-serialized structured rendering data (resource cards, properties, etc.)
    /// </summary>
    public string? RichContent { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? TokenUsage { get; set; }
}
