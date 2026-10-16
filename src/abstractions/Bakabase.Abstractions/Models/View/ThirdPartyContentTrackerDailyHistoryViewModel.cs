namespace Bakabase.Abstractions.Models.View;

public record ThirdPartyContentTrackerDailyHistoryDayViewModel
{
    public string Date { get; set; } = null!;
    public int Count { get; set; }
}

/// <summary>Previously viewed content grouped by its publication day, including empty days.</summary>
public record ThirdPartyContentTrackerDailyHistoryViewModel
{
    public List<ThirdPartyContentTrackerDailyHistoryDayViewModel> Days { get; set; } = [];
}
