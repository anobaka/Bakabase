using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliFavoriteItemClassifierTests
{
    private static Dictionary<long, BilibiliFavoriteItemClassification> ClassifyMixed() =>
        BilibiliFixtures.Data<FavoriteItemSearchResponseData>("fav-items-mixed.json").Medias!
            .ToDictionary(i => i.Id, BilibiliFavoriteItemClassifier.Classify);

    [DataTestMethod]
    [DataRow(1001L, BilibiliFavoriteItemKind.Video, null, DisplayName = "(2,0) video")]
    [DataRow(1002L, BilibiliFavoriteItemKind.Video, BilibiliSkipReason.InvalidItem, DisplayName = "(2,1) invalid")]
    [DataRow(1009L, BilibiliFavoriteItemKind.Video, BilibiliSkipReason.InvalidItem, DisplayName = "(2,9) deleted by uploader")]
    [DataRow(1004L, BilibiliFavoriteItemKind.Video, null, DisplayName = "(2,4) login-only legacy is valid")]
    [DataRow(1003L, BilibiliFavoriteItemKind.Video, BilibiliSkipReason.InteractiveVideo, DisplayName = "(2,16) interactive")]
    [DataRow(1005L, BilibiliFavoriteItemKind.Video, null, DisplayName = "(2,2) PGC archive is valid")]
    [DataRow(1006L, BilibiliFavoriteItemKind.Video, null, DisplayName = "type missing is a video")]
    [DataRow(321808L, BilibiliFavoriteItemKind.OgvEpisode, BilibiliSkipReason.UnsupportedOgvEpisode, DisplayName = "(24,0)")]
    [DataRow(281830L, BilibiliFavoriteItemKind.OgvEpisode, BilibiliSkipReason.InvalidItem, DisplayName = "(24,1) removed OGV")]
    [DataRow(518946L, BilibiliFavoriteItemKind.Audio, BilibiliSkipReason.UnsupportedAudio, DisplayName = "(12,0)")]
    [DataRow(7001L, BilibiliFavoriteItemKind.UgcSeason, BilibiliSkipReason.UnsupportedCollection, DisplayName = "(21,0)")]
    [DataRow(9901L, BilibiliFavoriteItemKind.Unknown, BilibiliSkipReason.UnsupportedItemType, DisplayName = "(99,0)")]
    [DataRow(1007L, BilibiliFavoriteItemKind.Video, null, DisplayName = "attr 0 titled 已失效视频 is still a video")]
    public void EveryRow(long id, BilibiliFavoriteItemKind kind, BilibiliSkipReason? reason)
    {
        var c = ClassifyMixed()[id];
        Assert.AreEqual(kind, c.Kind);
        Assert.AreEqual(reason, c.Skip?.Reason);
    }

    [TestMethod]
    public void AnUnknownTypeCarriesTheTypeAsCode()
    {
        Assert.AreEqual(99, ClassifyMixed()[9901].Skip!.Code);
    }

    [TestMethod]
    public void TheTitleIsNeverRead()
    {
        var valid = new FavoriteItem {Id = 1, Type = 2, Attr = 0, Title = "已失效视频"};
        var invalid = new FavoriteItem {Id = 2, Type = 2, Attr = 1, Title = "一个真实的标题"};
        Assert.IsNull(BilibiliFavoriteItemClassifier.Classify(valid).Skip);
        Assert.AreEqual(BilibiliSkipReason.InvalidItem, BilibiliFavoriteItemClassifier.Classify(invalid).Skip!.Reason);
    }
}
