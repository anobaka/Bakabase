using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Naming
{
    public class ExHentaiNamingFields
    {
        [DownloaderNamingField(Example = "2486997", Description = "Id of gallery.")]
        public const string GalleryId = nameof(GalleryId);

        [DownloaderNamingField(Example = "fb8cf251c5", Description = "Token of gallery.")]
        public const string GalleryToken = nameof(GalleryToken);

        [DownloaderNamingField(Example = "[村上水軍の館 (村上水軍)] インチキコーチとテニスで勝負。負けたらひと晩コーチの性玩具(おもちゃ)",
            Description = "Name displayed in detail page. If RawName is empty, Name will be used instead.")]
        public const string RawName = nameof(RawName);

        [DownloaderNamingField(
            Example =
                "[Murakami Suigun no Yakata (Murakami Suigun)] Inchiki Coach to Tennis de Shoubu. Maketara Hitoban Coach no Omocha",
            Description = "Name displayed directly.")]
        public const string Name = nameof(Name);

        [DownloaderNamingField(Example = "ArtistCG")]
        public const string Category = nameof(Category);
        //
        // [DownloaderNamingField(Example = "240", Description = "Total pages")]
        // public const string FileCount = nameof(FileCount);
        //
        // [DownloaderNamingField(Example = "4.62", Description = "Score")]
        // public const string Rate = nameof(Rate);

        [DownloaderNamingField(Example = "6756759_5179917_0",
            Description = "Current page title without file extension.")]
        public const string PageTitle = nameof(PageTitle);

        [DownloaderNamingField(Example = ".png",
            Description = "Extension of file with leading dot.")]
        public const string Extension = nameof(Extension);
    }
}