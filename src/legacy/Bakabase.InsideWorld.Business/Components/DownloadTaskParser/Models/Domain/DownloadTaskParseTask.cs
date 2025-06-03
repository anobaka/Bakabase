using Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.Constants;
using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain;

public record DownloadTaskParseTask
{
    public int Id { get; set; }
    public DownloadTaskParserSource Source { get; set; }
    public string Link { get; set; } = null!;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public DateTime? ParsedAt { get; set; }
    public List<Item>? Items { get; set; }
    public string? Error { get; set; }

    public record Item
    {
        public string Title { get; set; } = null!;
        public string? Link { get; set; }
        public string? AccessCode { get; set; }
        public string? DecompressionPassword { get; set; }
    }
}