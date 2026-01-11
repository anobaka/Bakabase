using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Comparison.Models.Db;

public record ComparisonPlanDbModel
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// 搜索条件 JSON（存储 ResourceSearchDbModel）
    /// </summary>
    public string? SearchJson { get; set; }
    public double Threshold { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastRunAt { get; set; }
}
