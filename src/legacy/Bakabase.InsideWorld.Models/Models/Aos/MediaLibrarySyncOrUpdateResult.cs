namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record class MediaLibrarySyncOrUpdateResult : ResourceRangeAddOrUpdateResult
    {
        public int RemovedCount { get; set; }
        public int UpdatedCount { get; set; }
    }
}
