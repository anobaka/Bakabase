using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Models.View
{
    public class FilePlayabilityViewModel
    {
        /// <summary>
        /// Whether the file is playable
        /// </summary>
        public bool Playable { get; set; }

        /// <summary>
        /// Detected media type
        /// </summary>
        public MediaType MediaType { get; set; }

        /// <summary>
        /// Video/Audio codec name (e.g., h264, hevc, aac)
        /// </summary>
        public string? Codec { get; set; }

        /// <summary>
        /// Duration in seconds
        /// </summary>
        public double? Duration { get; set; }

        /// <summary>
        /// Video width in pixels
        /// </summary>
        public int? Width { get; set; }

        /// <summary>
        /// Video height in pixels
        /// </summary>
        public int? Height { get; set; }

        /// <summary>
        /// Error message if not playable
        /// </summary>
        public string? Error { get; set; }
    }
}
