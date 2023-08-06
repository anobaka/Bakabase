using System;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.Constants
{
    /// <summary>
    /// 
    /// </summary>
    [Flags]
    public enum ExHentaiCategory
    {
        Unknown = 0,
        Misc = 1,
        Doushijin = 2,
        Manga = 4,
        ArtistCG = 8,
        GameCG = 16,
        ImageSet = 32,
        Cosplay = 64,
        AsianPorn = 128,
        NonH = 256,
        Western = 512,
    }
}