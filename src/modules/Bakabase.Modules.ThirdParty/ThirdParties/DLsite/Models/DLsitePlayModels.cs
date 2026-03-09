using System.Text.Json.Serialization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;

/// <summary>
/// Response from GET /api/v3/content/count
/// </summary>
public class DLsitePlayContentCount
{
    [JsonPropertyName("user")]
    public int User { get; set; }

    [JsonPropertyName("page_limit")]
    public int PageLimit { get; set; }

    [JsonPropertyName("concurrency")]
    public int Concurrency { get; set; }
}

/// <summary>
/// Item from GET /api/v3/content/sales
/// </summary>
public class DLsitePlaySaleItem
{
    [JsonPropertyName("workno")]
    public string Workno { get; set; } = null!;

    [JsonPropertyName("sales_date")]
    public string? SalesDate { get; set; }
}

/// <summary>
/// Maker info from POST /api/v3/content/works
/// </summary>
public class DLsitePlayMaker
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public DLsitePlayLocalizedName? Name { get; set; }
}

/// <summary>
/// Localized name (Japanese / English)
/// </summary>
public class DLsitePlayLocalizedName
{
    [JsonPropertyName("ja_JP")]
    public string? JaJp { get; set; }

    [JsonPropertyName("en_US")]
    public string? EnUs { get; set; }

    [JsonPropertyName("zh_CN")]
    public string? ZhCn { get; set; }

    [JsonPropertyName("zh_TW")]
    public string? ZhTw { get; set; }

    [JsonPropertyName("ko_KR")]
    public string? KoKr { get; set; }

    /// <summary>
    /// Returns the best available name, preferring ja_JP then zh_CN then en_US.
    /// </summary>
    public string? GetBestName() =>
        JaJp ?? ZhCn ?? EnUs ?? ZhTw ?? KoKr;
}

/// <summary>
/// Work detail from POST /api/v3/content/works
/// </summary>
public class DLsitePlayWorkDetail
{
    [JsonPropertyName("workno")]
    public string Workno { get; set; } = null!;

    [JsonPropertyName("name")]
    public DLsitePlayLocalizedName? Name { get; set; }

    [JsonPropertyName("maker")]
    public DLsitePlayMaker? Maker { get; set; }

    [JsonPropertyName("work_type")]
    public string? WorkType { get; set; }

    [JsonPropertyName("age_category")]
    public string? AgeCategory { get; set; }

    [JsonPropertyName("regist_date")]
    public string? RegistDate { get; set; }

    [JsonPropertyName("sales_date")]
    public string? SalesDate { get; set; }

    [JsonPropertyName("upgrade_date")]
    public string? UpgradeDate { get; set; }

    [JsonPropertyName("work_files")]
    public DLsitePlayWorkFiles? WorkFiles { get; set; }
}

/// <summary>
/// Response wrapper from POST /api/v3/content/works
/// </summary>
public class DLsitePlayWorksResponse
{
    [JsonPropertyName("works")]
    public List<DLsitePlayWorkDetail>? Works { get; set; }
}

/// <summary>
/// Work files (cover images etc.)
/// </summary>
public class DLsitePlayWorkFiles
{
    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("sam")]
    public string? Sam { get; set; }
}

/// <summary>
/// Response from GET /api/v3/download
/// </summary>
public class DLsiteDownloadResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("files")]
    public List<DLsiteDownloadFile>? Files { get; set; }
}

public class DLsiteDownloadFile
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

/// <summary>
/// Parsed download link for a DLsite work.
/// </summary>
public class DLsiteDownloadLink
{
    public string Url { get; set; } = null!;
    public string FileName { get; set; } = null!;
}

/// <summary>
/// Result of resolving download links, including optional DRM key.
/// </summary>
public class DLsiteDownloadInfo
{
    public List<DLsiteDownloadLink> Links { get; set; } = [];
    public string? DrmKey { get; set; }
}
