namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// 媒体库-资源映射领域模型
/// </summary>
public class MediaLibraryResourceMapping
{
    public int Id { get; set; }
    public int MediaLibraryId { get; set; }
    public int ResourceId { get; set; }
    public DateTime CreateDt { get; set; }
}
