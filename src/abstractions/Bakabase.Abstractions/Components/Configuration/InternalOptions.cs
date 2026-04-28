using System.Collections.Immutable;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace Bakabase.Abstractions.Components.Configuration
{
    public static class InternalOptions
    {
        public static readonly ImmutableHashSet<string> ImageExtensions =
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
                ".bmp",
                ".gif",
                ".ico",
                ".jpeg",
                ".jpg",
                ".png",
                // ".psd",
                ".tiff",
                ".webp",
                ".svg"
            );

        public static readonly ImmutableHashSet<string> VideoExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".mp4",
            ".avi",
            ".mkv",
            ".wmv",
            ".flv",
            ".ts",
            ".webm",
            ".mpeg",
            ".rmvb",
            "3gp",
            "mov"
        );

        public static readonly ImmutableHashSet<string> AudioExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".mp3",
            ".flac",
            ".m4a",
            ".mid",
            ".midi",
            ".ogg",
            ".wav",
            ".weba"
        );

        public static readonly ImmutableHashSet<string> CompressedFileExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".rar",
            ".7z",
            ".zip",
            ".tar",
            ".bz2",
            ".gz",
            ".tgz"
        );
        
        public static readonly ImmutableHashSet<string> CompoundCompressedFileExtensions =
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
                ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.Z", ".tar.lz", ".tar.lzma", ".tar.tgz");

        public static readonly ImmutableHashSet<string> TextExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".txt"
        );

        public static readonly Dictionary<MediaType, ImmutableHashSet<string>> MediaTypeExtensions = new()
        {
            {
                MediaType.Image, ImageExtensions
            },
            {
                MediaType.Audio, AudioExtensions
            },
            {
                MediaType.Video, VideoExtensions
            },
            {
                MediaType.Text, TextExtensions
            },
            {
                MediaType.Application, [ExeExtension]
            }
        };

        public const string SevenZipCompressedFileExtension = ".7z";
        public const string IcoFileExtension = ".ico";

        public const char DirSeparator = '/';
        public const char WindowsSpecificDirSeparator = '\\';
        public static readonly string UncPathPrefix = $"{DirSeparator}{DirSeparator}";

        public static readonly string WindowsSpecificUncPathPrefix =
            $"{WindowsSpecificDirSeparator}{WindowsSpecificDirSeparator}";

        public const char CompressedFileRootSeparator = '!';
        public const string RegexForOnePathLayer = @"[^\/]+";

        public const string TextSeparator = ",";
        public const string LayerTextSeparator = "/";
        public const string ExeExtension = ".exe";

        public static class HttpClientNames
        {
            public const string JavLibrary = nameof(JavLibrary);
            public const string Bilibili = nameof(Bilibili);
            public const string ExHentai = nameof(ExHentai);
            public const string Pixiv = nameof(Pixiv);
            public const string Bangumi = nameof(Bangumi);
            public const string DLsite = nameof(DLsite);
            public const string Default = nameof(Default);
            public const string SoulPlus = nameof(SoulPlus);
            public const string Tmdb = nameof(Tmdb);
        }

        public static string DefaultHttpUserAgent =
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Bakabase/{AppService.CoreVersion}";

        public const string TempDirectoryName = "temp";
        public const string ComponentInfoFileName = "i.json";

        public const int MaxThumbnailWidth = 600;
        public const int MaxThumbnailHeight = 600;

        public const int DefaultSearchPageSize = 100;

        public const int MaxFallbackCoverCount = 10;
        public const int MaxPlayableFilesPerTypeAndSubDir = 3;

        public const string ResourceMarkerFileName = ".bakabase.json";
    }
}