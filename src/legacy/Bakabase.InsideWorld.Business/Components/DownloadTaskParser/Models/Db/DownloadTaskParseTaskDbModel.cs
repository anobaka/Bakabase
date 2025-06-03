using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using System;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Db;

public record DownloadTaskParseTaskDbModel
{
    public int Id { get; set; }
    public DownloadTaskParserSource Source { get; set; }
    public string Link { get; set; } = null!;
    public string? Title { get; set; }
    public DateTime? ParsedAt { get; set; }
    public string? Items { get; set; }
    public string? Error { get; set; }
}