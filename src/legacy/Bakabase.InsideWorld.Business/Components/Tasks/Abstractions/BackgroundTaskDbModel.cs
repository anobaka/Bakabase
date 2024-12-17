using System;

namespace Bakabase.InsideWorld.Business.Components.Tasks.Abstractions;

public record BackgroundTaskDbModel
{
    public int Id { get; set; }
    public string DescriptorId { get; set; } = null!;
    public string? Args { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}