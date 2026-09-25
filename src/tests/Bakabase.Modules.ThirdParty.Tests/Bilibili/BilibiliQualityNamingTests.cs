using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliQualityNamingTests
{
    [TestMethod]
    public void TheLegacyNameIsTheHighestSupportFormatOfTheNamingAnswer()
    {
        Assert.AreEqual("1080P 高清", BilibiliQualityNaming.LegacyQualityName(
            BilibiliFixtures.Data<VideoSource>("playurl-16-naming.json").SupportFormats));
        Assert.AreEqual("4K 超高清", BilibiliQualityNaming.LegacyQualityName(
            BilibiliFixtures.Data<VideoSource>("playurl-16-naming-4k.json").SupportFormats));
        Assert.IsNull(BilibiliQualityNaming.LegacyQualityName(null));
        Assert.IsNull(BilibiliQualityNaming.LegacyQualityName([]));
    }

    [TestMethod]
    public void TheNamingUrlIsByteIdenticalToTheOneLibrariesWereNamedWith()
    {
        Assert.AreEqual(
            "https://api.bilibili.com/x/player/playurl?avid=1001&cid=5001&bvid=&qn=16&type=&otype=json&fourk=1&fnval=16",
            BiliBiliApiUrls.LegacyNamingPlayUrl(1001, 5001));
    }

    [TestMethod]
    public void DescribeQuality()
    {
        var formats = BilibiliFixtures.Data<VideoSource>("playurl-dash.json").SupportFormats;
        Assert.AreEqual("1080P 高清", BilibiliQualityNaming.DescribeQuality(80, formats));
        Assert.AreEqual("Q74", BilibiliQualityNaming.DescribeQuality(74, formats));
        Assert.AreEqual("Q80", BilibiliQualityNaming.DescribeQuality(80, null));
    }
}
