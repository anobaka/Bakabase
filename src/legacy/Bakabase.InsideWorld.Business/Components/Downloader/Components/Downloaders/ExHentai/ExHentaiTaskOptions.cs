namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    public class ExHentaiTaskOptions
    {
        public bool PreferTorrent { get; set; } = true;
    }

    /// <summary>Nullable mirror of ExHentaiTaskOptions for parsing: tells an absent value apart from an explicit one. Keep members in sync.</summary>
    internal class ExHentaiTaskOptionsPatch
    {
        public bool? PreferTorrent { get; set; }
    }
}
