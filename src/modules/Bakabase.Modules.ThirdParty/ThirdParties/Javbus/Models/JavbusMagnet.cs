namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

/// <summary>One row of Javbus' magnet table for a given code.</summary>
public record JavbusMagnet
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Size exactly as the site prints it (e.g. <c>4.35GB</c>).</summary>
    public string? Size { get; init; }

    /// <summary><see cref="Size"/> parsed into bytes; 0 when the site printed something unparsable.</summary>
    public long SizeInBytes { get; init; }

    public string? Date { get; init; }

    public string Link { get; init; } = string.Empty;

    public JavbusMagnetTag Tag { get; init; }
}
