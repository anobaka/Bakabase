using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;

namespace InsideWorld.Migrations.Legacies
{
    [Obsolete]
    internal sealed class InsideWorldAppOptions
    {
        public string Language { get; set; }
        public string Version { get; set; } = "0.0.0";
        public bool EnablePreReleaseChannel { get; set; }
        public bool EnableAnonymousDataTracking { get; set; } = true;
        public string WwwRootPath { get; set; }
        public string DataPath { get; set; }
        public string PrevDataPath { get; set; }
        public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Prompt;
        [Obsolete] public bool? MinimizeOnClose { get; set; }

        [Obsolete] public int ResourceColCount { get; set; } = 8;
        [Obsolete] public DateTime LastSyncDt { get; set; }
        [Obsolete] public string JavLibraryCookie { get; set; }
        [Obsolete] public string JavLibraryDownloadPath { get; set; }
        [Obsolete] public string[] JavLibraryUrls { get; set; } = new string[] { };
        [Obsolete] public string[] JavLibraryTorrentLinkKeywords { get; set; } = new string[] { };

        [Obsolete] public string BiliBiliDownloadPath { get; set; }
        [Obsolete] public string BiliBiliCookie { get; set; }
        [Obsolete] public int[] BiliBiliFavoritesToDownloadIds { get; set; }
        [Obsolete] public int BiliBiliFavoritesToArchiveId { get; set; }
        [Obsolete] public bool BiliBiliSchedulerEnabled { get; set; }

        [Obsolete] public DateTime LastNfoGenerationDt { get; set; }
        [Obsolete] public TimeSpan NfoGenerationInterval { get; set; } = TimeSpan.FromHours(1);
        [Obsolete] public string FFmpegBinDirectory { get; set; }

        [Obsolete]
        public AdditionalCoverDiscoveringSource[] AdditionalCoverDiscoveringSources { get; set; } =
            new AdditionalCoverDiscoveringSource[] { };

        [Obsolete] public string FileExplorerWorkingDirectory { get; set; }
        [Obsolete] public string[] RecentMovingDestinations { get; set; } = Array.Empty<string>();

        [Obsolete]
        public ThirdPartyOptions.SimpleSearchEngineOptions[] SimpleSearchEngines { get; set; } =
            new ThirdPartyOptions.SimpleSearchEngineOptions[] { };

        [Obsolete] public string ExHentaiCookie { get; set; }
        [Obsolete] public string[] ExHentaiExcludedTags { get; set; } = new string[] { };
        [Obsolete] public string[] ExHentaiLinks { get; set; } = new string[] { };
        [Obsolete] public string ExHentaiDownloadPath { get; set; }
        [Obsolete] public int ExHentaiDownloadThreads { get; set; } = 5;
    }
}