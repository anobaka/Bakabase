using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs;

[Options]
public record SoulPlusOptions
{
    public string? Cookie { get; set; }
    public int AutoBuyThreshold { get; set; } = 5;
}