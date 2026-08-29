using System;
using System.Collections.Generic;
using System.IO;

namespace Bakabase.Service.Components.Playback
{
    public enum VideoDeliveryMethod
    {
        /// <summary>
        /// Send the file's own bytes. Cheapest, and the only method that supports
        /// seeking, because the response carries real ranges.
        /// </summary>
        DirectPlay = 0,

        /// <summary>
        /// Repackage the existing streams into fragmented MP4 without re-encoding.
        /// Costs almost nothing and loses no quality; used when the picture is fine
        /// but the wrapper (or the audio track) is not something a browser accepts.
        /// </summary>
        Remux = 1,

        /// <summary>
        /// Re-encode the video. Expensive, lossy, and the reason remote playback is
        /// better handed to a native player.
        /// </summary>
        Transcode = 2
    }

    /// <param name="Method">How to deliver the video.</param>
    /// <param name="CopyAudio">
    /// For <see cref="VideoDeliveryMethod.Remux"/>: whether the audio track can be
    /// copied as-is, or has to be re-encoded to AAC.
    /// </param>
    /// <param name="Reason">Short explanation, for logs and diagnostics.</param>
    public record VideoDeliveryPlan(VideoDeliveryMethod Method, bool CopyAudio, string Reason);

    /// <summary>
    /// Chooses how to hand a video file to a browser.
    /// <para>
    /// Kept as a pure function because this is exactly the logic that regresses
    /// silently: a wrong decision does not throw, it just produces a black player or
    /// a video with no sound on someone else's machine.
    /// </para>
    /// </summary>
    public static class VideoDeliveryPlanner
    {
        /// <summary>
        /// Containers a browser will actually demux. MKV is deliberately absent:
        /// Chrome demuxes it only sometimes, and Firefox and Safari not at all, which
        /// is why an h264-in-MKV file has to be remuxed rather than sent as-is.
        /// </summary>
        private static readonly HashSet<string> BrowserSafeContainers =
            new(StringComparer.OrdinalIgnoreCase) {".mp4", ".m4v", ".webm", ".ogv"};

        /// <summary>
        /// Video codecs we send without re-encoding.
        /// <para>
        /// Only H.264 for now, matching what Bakabase has always direct-played.
        /// HEVC and AV1 decode only on hardware that supports them, so choosing them
        /// safely needs the client to declare its capabilities — until that exists,
        /// widening this set would turn working playback into a black player.
        /// </para>
        /// </summary>
        private static readonly HashSet<string> PassThroughVideoCodecs =
            new(StringComparer.OrdinalIgnoreCase) {"h264", "avc1", "avc"};

        /// <summary>
        /// Audio codecs a browser can decode. AC3/E-AC3 and DTS are the notable
        /// absentees, and the common cause of "the video plays but there is no sound"
        /// — they need re-encoding even when the picture does not.
        /// </summary>
        private static readonly HashSet<string> PassThroughAudioCodecs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "aac", "mp4a", "mp3", "mp3float", "opus", "vorbis", "flac", "alac",
                "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_f64le",
                "pcm_u8", "pcm_s16be", "pcm_s24be", "pcm_s32be"
            };

        /// <param name="fileNameOrExtension">The file's name or bare extension.</param>
        /// <param name="videoCodec">ffprobe's codec_name for the first video stream.</param>
        /// <param name="audioCodec">
        /// ffprobe's codec_name for the first audio stream; null or empty for a file
        /// with no audio, which never blocks direct play.
        /// </param>
        public static VideoDeliveryPlan Plan(string? fileNameOrExtension, string? videoCodec, string? audioCodec)
        {
            if (!IsPassThroughVideo(videoCodec))
            {
                return new VideoDeliveryPlan(VideoDeliveryMethod.Transcode, false,
                    $"video codec '{Describe(videoCodec)}' is not sent to browsers as-is");
            }

            var audioIsFine = IsPassThroughAudio(audioCodec);

            if (!IsBrowserSafeContainer(fileNameOrExtension))
            {
                return new VideoDeliveryPlan(VideoDeliveryMethod.Remux, audioIsFine,
                    $"container '{Describe(GetExtension(fileNameOrExtension))}' is not a browser container");
            }

            if (!audioIsFine)
            {
                return new VideoDeliveryPlan(VideoDeliveryMethod.Remux, false,
                    $"audio codec '{Describe(audioCodec)}' needs re-encoding");
            }

            return new VideoDeliveryPlan(VideoDeliveryMethod.DirectPlay, true, "container and codecs play as-is");
        }

        private static bool IsPassThroughVideo(string? codec) =>
            !string.IsNullOrWhiteSpace(codec) && PassThroughVideoCodecs.Contains(codec.Trim());

        /// <summary>
        /// A file with no audio stream at all is fine to send as-is; only a track we
        /// cannot decode forces a remux.
        /// </summary>
        private static bool IsPassThroughAudio(string? codec) =>
            string.IsNullOrWhiteSpace(codec) || PassThroughAudioCodecs.Contains(codec.Trim());

        private static bool IsBrowserSafeContainer(string? fileNameOrExtension)
        {
            var extension = GetExtension(fileNameOrExtension);
            return extension != null && BrowserSafeContainers.Contains(extension);
        }

        private static string? GetExtension(string? fileNameOrExtension)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrExtension))
            {
                return null;
            }

            var value = fileNameOrExtension.Trim();
            var extension = value.StartsWith('.') && !value.Contains('/') && !value.Contains('\\')
                ? value
                : Path.GetExtension(value);

            return string.IsNullOrEmpty(extension) ? null : extension;
        }

        private static string Describe(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }
}
