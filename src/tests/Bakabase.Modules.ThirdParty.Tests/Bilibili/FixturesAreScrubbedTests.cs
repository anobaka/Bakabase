using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

/// <summary>
/// The Bilibili fixtures are hand-built. Real answers carry the prober's IP (<c>oi=</c>, and PCDN host names), account
/// ids and live signatures; this guard fails if any of that is ever pasted in.
/// </summary>
[TestClass]
public class FixturesAreScrubbedTests
{
    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        ("oi= query value", new Regex(@"\boi=\d", RegexOptions.IgnoreCase)),
        ("mid= query value", new Regex(@"\bmid=\d", RegexOptions.IgnoreCase)),
        ("IPv4 address", new Regex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")),
        ("real upsig", new Regex(@"upsig=(?!0{32}\b)[0-9a-f]{32}", RegexOptions.IgnoreCase)),
        ("real auth_key", new Regex(@"auth_key=(?!1-0-0-0\b)", RegexOptions.IgnoreCase)),
        ("trid", new Regex(@"\btrid=", RegexOptions.IgnoreCase)),
        ("SESSDATA", new Regex("SESSDATA", RegexOptions.IgnoreCase)),
        ("bili_jct", new Regex("bili_jct", RegexOptions.IgnoreCase)),
        ("buvid", new Regex("buvid", RegexOptions.IgnoreCase)),
    ];

    [TestMethod]
    public void NoFixtureCarriesRealIdentifiersOrSignatures()
    {
        var files = Directory.GetFiles(BilibiliFixtures.DirectoryPath, "*", SearchOption.AllDirectories);
        Assert.IsTrue(files.Length > 0);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var (name, pattern) in Forbidden)
            {
                var match = pattern.Match(text);
                Assert.IsFalse(match.Success, $"{Path.GetFileName(file)} contains a {name}: '{match.Value}'");
            }
        }
    }

    [TestMethod]
    public void TheGuardCatchesRealLookingData()
    {
        string[] leaks =
        [
            "https://upos-sz-mirrorcos.bilivideo.com/a.m4s?e=1&oi=1234567890&upsig=0123456789abcdef0123456789abcdef",
            "\"ip\":\"203.0.113.7\"",
            "?auth_key=1790245698-3bc90f0dafea4e3c-0-8ec97777e295",
            "Cookie: SESSDATA=x",
        ];
        foreach (var leak in leaks)
        {
            Assert.IsTrue(Forbidden.Any(f => f.Pattern.IsMatch(leak)), leak);
        }

        Assert.IsFalse(Forbidden.Any(f => f.Pattern.IsMatch("av01.0.08M.08.0.110.01.01.01.0")), "codec strings are not IPs");
    }
}
