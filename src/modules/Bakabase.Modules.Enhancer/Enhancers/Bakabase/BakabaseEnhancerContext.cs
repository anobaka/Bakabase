namespace Bakabase.Modules.Enhancer.Enhancers.Bakabase
{
    public record BakabaseEnhancerContext
    {
        public string? Name { get; set; }
        public string? Series { get; set; }
        public List<string>? Publishers { get; set; }
        public DateTime? ReleaseDt { get; set; }
        public string? VolumeName { get; set; }
        public string? VolumeTitle { get; set; }
        public List<string>? Originals { get; set; }
        public string? Language { get; set; }
    }
}
