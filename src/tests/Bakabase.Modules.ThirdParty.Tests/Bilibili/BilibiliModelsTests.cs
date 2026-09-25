using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliModelsTests
{
    private static readonly (string Prefix, Type Data)[] Shapes =
    [
        ("myinfo", typeof(UserCredential)),
        ("favorites-list", typeof(FavoritesList)),
        ("fav-items", typeof(FavoriteItemSearchResponseData)),
        ("view", typeof(Post)),
        ("pagelist", typeof(List<PostPage>)),
        ("playurl", typeof(VideoSource)),
        ("dmview", typeof(DmView)),
    ];

    [TestMethod]
    public void EveryFixtureDeserializes()
    {
        var files = Directory.GetFiles(BilibiliFixtures.DirectoryPath, "*.json");
        Assert.IsTrue(files.Length > 40, "fixtures were not copied next to the tests");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var json = File.ReadAllText(file);
            if (name.StartsWith("subtitle-"))
            {
                Assert.IsNotNull(JsonConvert.DeserializeObject<SubtitleBody>(json)!.Body, name);
                continue;
            }

            var shape = Shapes.FirstOrDefault(s => name.StartsWith(s.Prefix + "-"));
            Assert.IsNotNull(shape.Data, $"no shape for {name}");
            var wrapper = JsonConvert.DeserializeObject(json, typeof(DataWrapper<>).MakeGenericType(shape.Data));
            Assert.IsNotNull(wrapper, name);
        }
    }

    [TestMethod]
    public void SixteenDigitMids()
    {
        Assert.AreEqual(3546571234567890L,
            BilibiliFixtures.Data<UserCredential>("myinfo-16digit.json").Profile!.Mid);
    }

    [TestMethod]
    public void FlacAudioIsAnObjectDolbyAudioAnArray()
    {
        var dash = BilibiliFixtures.Data<VideoSource>("playurl-dash-hires.json").Dash!;
        Assert.AreEqual(30251, dash.Flac!.Audio!.Id);
        Assert.AreEqual(30250, dash.Dolby!.Audio!.Single().Id);
    }

    [TestMethod]
    public void NumericStringsAreAccepted()
    {
        var data = BilibiliFixtures.Data<VideoSource>("playurl-quality-string.json");
        Assert.AreEqual(80, data.Quality);
        Assert.AreEqual(187106L, data.Timelength);
        Assert.AreEqual(80, data.SupportFormats!.Single().Quality);
    }

    [TestMethod]
    public void FavoriteItemFields()
    {
        var items = BilibiliFixtures.Data<FavoriteItemSearchResponseData>("fav-items-mixed.json").Medias!;
        var ogv = items.Single(i => i.Id == 321808);
        Assert.AreEqual(24, ogv.Type);
        Assert.AreEqual(33333L, ogv.Ogv!.SeasonId);
        Assert.IsNull(ogv.Ugc);
        Assert.AreEqual(0, items.Single(i => i.Id == 1006).Type, "missing type reads as 0");
        Assert.AreEqual(2, items.Single(i => i.Id == 1001).Page);
        Assert.AreEqual(5001L, items.Single(i => i.Id == 1001).Ugc!.FirstCid);
        Assert.IsNull(BilibiliFixtures.Data<FavoriteItemSearchResponseData>("fav-items-medias-null.json").Medias);
    }

    [TestMethod]
    public void ViewFields()
    {
        var view = BilibiliFixtures.Data<Post>("view-upower-free.json");
        Assert.IsTrue(view.IsUpowerExclusive);
        Assert.IsTrue(view.IsUpowerPlay);
        Assert.AreEqual(1, view.Rights!.Pay);
        Assert.AreEqual(1001L, BilibiliFixtures.Data<Post>("view-forward.json").Forward);
        Assert.AreEqual(120, BilibiliFixtures.Data<Post>("view-ok-2pages.json").Pages![0].Duration);
    }
}
