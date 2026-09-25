namespace Bakabase.InsideWorld.Business.Components.Dependency.Abstractions
{
    public record DependentComponentVersion
    {
        public string Version { get; set; } = null!;
        public string? Description { get; set; }
        public bool CanUpdate { get; set; }

        /// <summary>
        /// False when a version is installed but cannot be read (e.g. a git-date ffmpeg build), so nothing was
        /// compared: <see cref="CanUpdate"/> is false then without meaning "up to date".
        /// </summary>
        public bool InstalledVersionRecognized { get; set; } = true;

        public static DependentComponentVersion Unknown => new DependentComponentVersion();
    }
}