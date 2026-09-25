using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliDiagnosticsTests
{
    [TestMethod]
    public void RedactJsonStripsQueriesAndIds()
    {
        const string body =
            "{\"base_url\":\"https://upos-sz-example.bilivideo.com/upgcxcode/00/5001-1-100026-80.m4s?deadline=1900000000&oi=1234567&mid=987654&upsig=abcdef\"," +
            "\"mid\":3546571234567890,\"oi\":\"1234567\",\"uid\": 42," +
            "\"subtitle_url\":\"//aisubtitle.hdslb.com/bfs/subtitle/0001.json?auth_key=1790245698-abc-0-def\"}";
        var redacted = BilibiliDiagnostics.RedactJson(body);
        StringAssert.Contains(redacted, "https://upos-sz-example.bilivideo.com/…/5001-1-100026-80.m4s");
        StringAssert.Contains(redacted, "https://aisubtitle.hdslb.com/…/0001.json");
        foreach (var secret in new[] {"deadline=", "oi=", "upsig=", "auth_key=", "3546571234567890", "1234567", "987654", "42"})
        {
            Assert.IsFalse(redacted.Contains(secret), $"{secret} survived: {redacted}");
        }

        StringAssert.Contains(redacted, "\"mid\":0");
    }

    [TestMethod]
    public void RedactJsonTruncates()
    {
        Assert.AreEqual("0123456789…", BilibiliDiagnostics.RedactJson(new string('x', 0) + "0123456789abcdef", 10));
        Assert.AreEqual("", BilibiliDiagnostics.RedactJson(null));
    }

    [TestMethod]
    public void RedactText()
    {
        var text = BilibiliDiagnostics.RedactText("see https://h.example.com/a/b.json?auth_key=1 now");
        Assert.AreEqual("see https://h.example.com/…/b.json now", text);
        Assert.AreEqual(201, BilibiliDiagnostics.RedactText(new string('x', 500)).Length);
    }
}
