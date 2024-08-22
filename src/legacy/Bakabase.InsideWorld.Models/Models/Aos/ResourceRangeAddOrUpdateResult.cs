namespace Bakabase.InsideWorld.Models.Models.Aos
{
    public record class ResourceRangeAddOrUpdateResult
    {
        public int AliasCount { get; set; }
        public int NewAliasCount { get; set; }
        public int PublisherCount { get; set; }
        public int NewPublisherCount { get; set; }
        public int ResourceCount { get; set; }
        public int NewResourceCount { get; set; }
        public int VolumeCount { get; set; }
        public int NewVolumeCount { get; set; }
        public int OriginalCount { get; set; }
        public int NewOriginalCount { get; set; }
    }
}