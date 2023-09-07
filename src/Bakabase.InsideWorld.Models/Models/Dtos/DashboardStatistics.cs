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
        public List<TextAndCount> CategoryResourceCounts { get; set; } = new();
        public List<TextAndCount> TodayAddedCategoryResourceCounts { get; set; } = new();
        public List<TextAndCount> ThisWeekAddedCategoryResourceCounts { get; set; } = new();
        public List<TextAndCount> ThisMonthAddedCategoryResourceCounts { get; set; } = new();
        public List<WeekCount> ResourceTrending { get; set; } = new();
        public List<PropertyAndCount> PropertyResourceCounts { get; set; } = new();

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

        public record PropertyAndCount(ResourceProperty Property, string? PropertyKey, string Value, int Count);

        public record DownloaderTaskCount(ThirdPartyId Id, DownloadTaskStatus Status, int TaskCount);
        public record ThirdPartyRequestCount(ThirdPartyId Id, int ResultType, int TaskCount);

        public record FileMoverInfo(int SourceCount, int TargetCount);

        public record WeekCount(int Offset, int Count);
    }
}