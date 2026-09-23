namespace Bakabase.Modules.Federation.Media;

/// <summary>Never let a peer select executable content types on the trusted local UI origin.</summary>
public static class MediaContentTypes
{
    public static (string Kind, string ContentType)? Get(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ("image", "image/jpeg"),
        ".png" => ("image", "image/png"),
        ".webp" => ("image", "image/webp"),
        ".gif" => ("image", "image/gif"),
        ".bmp" => ("image", "image/bmp"),
        ".avif" => ("image", "image/avif"),
        ".mp3" => ("audio", "audio/mpeg"),
        ".flac" => ("audio", "audio/flac"),
        ".wav" => ("audio", "audio/wav"),
        ".ogg" or ".opus" => ("audio", "audio/ogg"),
        ".m4a" => ("audio", "audio/mp4"),
        ".aac" => ("audio", "audio/aac"),
        ".wma" => ("audio", "audio/x-ms-wma"),
        ".mp4" or ".m4v" => ("video", "video/mp4"),
        ".mkv" => ("video", "video/x-matroska"),
        ".webm" => ("video", "video/webm"),
        ".mov" => ("video", "video/quicktime"),
        ".avi" => ("video", "video/x-msvideo"),
        ".wmv" => ("video", "video/x-ms-wmv"),
        ".ts" => ("video", "video/mp2t"),
        ".mpeg" or ".mpg" => ("video", "video/mpeg"),
        _ => null
    };
}
