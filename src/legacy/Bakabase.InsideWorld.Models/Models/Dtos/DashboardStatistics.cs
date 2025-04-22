using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
    public class DashboardStatistics
    {
        public List<CategoryMediaLibraryCount> CategoryMediaLibraryCounts { get; set; } = [];
        public List<TextAndCount> TodayAddedCategoryResourceCounts { get; set; } = new();
        public List<TextAndCount> ThisWeekAddedCategoryResourceCounts { get; set; } = new();
        public List<TextAndCount> ThisMonthAddedCategoryResourceCounts { get; set; } = new();
        public List<WeekCount> ResourceTrending { get; set; } = new();

        public List<TextAndCount> TagResourceCounts { get; set; } = new();
        public List<DownloaderTaskCount> DownloaderDataCounts { get; set; } = new();
        public List<ThirdPartyRequestCount> ThirdPartyRequestCounts { get; set; } = new();
        public FileMoverInfo FileMover { get; set; } = new(0, 0);
        public List<List<TextAndCount>> OtherCounts { get; set; } = new();


        public record TextAndCount(string? Name, int Count, string? Label = null)
        {
            public string? Label { get; set; } = Label;
            public string Name { get; set; } = Name ?? string.Empty;
            public int Count { get; set; } = Count;
        }

        public record CategoryMediaLibraryCount(string CategoryName, List<TextAndCount> MediaLibraryCounts);

        public record DownloaderTaskCount(ThirdPartyId Id, Dictionary<int, int> StatusAndCounts);

        public record ThirdPartyRequestCount(ThirdPartyId Id, int ResultType, int TaskCount);

        public record FileMoverInfo(int SourceCount, int TargetCount);

        public record WeekCount(int Offset, int Count);

        public int TotalExpectedPropertyValueCount { get; set; }
        public int TotalFilledPropertyValueCount { get; set; }
        public List<PropertyValueCoverage> PropertyValueCoverages { get; set; } = [];

        public record PropertyValueCoverage(int Pool, int Id, string Name, int FilledCount, int ExpectedCount);
    }
}