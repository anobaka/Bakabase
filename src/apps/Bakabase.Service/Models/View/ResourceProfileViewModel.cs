using System;
using System.Globalization;
using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Service.Models.View;

/// <summary>
/// 资源配置档案视图模型
/// </summary>
public record ResourceProfileViewModel
{
    public int Id { get; set; }

    /// <summary>Null keeps the original inline search; otherwise this profile uses a named reusable scope.</summary>
    public int? ResourceScopeId { get; set; }

    /// <summary>Null for this library; otherwise the stable identity of the resource owner.</summary>
    public string? SourceDeviceId { get; set; }

    /// <summary>Pins source-owned filter IDs to one incarnation of that library.</summary>
    public string? SourceLibraryEpoch { get; set; }

    /// <summary>Null inherits; browser, native or ask changes how this device opens matching resources.</summary>
    public string? OpeningMode { get; set; }

    /// <summary>
    /// 配置名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 搜索条件（使用 ViewModel 确保 DbValue 为序列化字符串）
    /// </summary>
    public ResourceSearchViewModel? Search { get; set; }

    /// <summary>
    /// 名称模板（可选），支持变量如 {Name}, {Layer1}, {Layer2} 等
    /// </summary>
    public string? NameTemplate { get; set; }

    /// <summary>
    /// 增强器配置
    /// </summary>
    public ResourceProfileEnhancerOptions? EnhancerOptions { get; set; }

    /// <summary>
    /// 可播放文件配置
    /// </summary>
    public ResourceProfilePlayableFileOptions? PlayableFileOptions { get; set; }

    /// <summary>
    /// 播放器配置
    /// </summary>
    public ResourceProfilePlayerOptions? PlayerOptions { get; set; }

    /// <summary>
    /// 属性配置
    /// </summary>
    public ResourceProfilePropertyOptions? PropertyOptions { get; set; }

    /// <summary>
    /// 优先级（多个 Profile 匹配同一资源时使用）
    /// </summary>
    public int Priority { get; set; }

    public DateTime CreatedAt { get; set; }
    // This value is an exact HTTP compare-and-swap token. The application's
    // ordinary millisecond date format must not truncate its database ticks.
    [JsonConverter(typeof(ResourceProfileUpdatedAtJsonConverter))]
    public DateTime UpdatedAt { get; set; }
}

public sealed class ResourceProfileUpdatedAtJsonConverter : JsonConverter<DateTime>
{
    public override bool CanRead => false;

    public override void WriteJson(JsonWriter writer, DateTime value, JsonSerializer serializer)
    {
        // SQLite restores DateTime without Kind; profile timestamps are stored in UTC.
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteValue(utc.ToString("O", CultureInfo.InvariantCulture));
    }

    public override DateTime ReadJson(JsonReader reader, Type objectType, DateTime existingValue, bool hasExistingValue,
        JsonSerializer serializer) => throw new NotSupportedException("Use the standard JSON ISO date reader.");
}
