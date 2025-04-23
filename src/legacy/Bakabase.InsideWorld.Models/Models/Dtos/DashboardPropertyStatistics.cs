using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Models.Dtos;

public record DashboardPropertyStatistics
{
    public int TotalExpectedPropertyValueCount { get; set; }
    public int TotalFilledPropertyValueCount { get; set; }
    public List<PropertyValueCoverage> PropertyValueCoverages { get; set; } = [];

    public record PropertyValueCoverage(int Pool, int Id, string Name, int FilledCount, int ExpectedCount);
}