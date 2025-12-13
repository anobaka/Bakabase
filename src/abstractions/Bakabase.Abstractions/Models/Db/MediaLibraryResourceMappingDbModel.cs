namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// 媒体库-资源映射数据库模型
/// </summary>
public record MediaLibraryResourceMappingDbModel
{
    public int Id { get; set; }
    public int MediaLibraryId { get; set; }
    public int ResourceId { get; set; }
    public DateTime CreateDt { get; set; }
}
