using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public record PostParserTask
{
    public int Id { get; set; }
    public PostParserSource Source { get; set; }
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