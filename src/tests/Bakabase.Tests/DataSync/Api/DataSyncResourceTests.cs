using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The server-side texts of data sync (§8.10.1, §9.4, §7.2.1) exist in English and Chinese with the same
/// placeholders, and never quote a device name (§11.5).
/// </summary>
[TestClass]
public partial class DataSyncResourceTests
{
    private static readonly string[] Keys =
    [
        ..new[] { "DataSync", "DataSyncApply", "DataSyncReview", "DataSyncResolve", "DataSyncUndo", "DataSyncRestore" }
            .SelectMany(task => new[] { $"BTask_Name_{task}", $"BTask_Description_{task}",
                $"BTask_MessageOnInterruption_{task}" }),
        "DataSync_Request_Title", "DataSync_Request_Body",
        ..new[] { "NeedsYou", "FollowOverride", "Paused", "Restore", "ReviewReady", "FirstSync", "Attention", "NewReader" }
            .SelectMany(c => new[] { $"DataSync_Notify_{c}_Title", $"DataSync_Notify_{c}_Body" }),
        ..new[] { "PeerReset", "PeerRestored", "MassDeletion", "KindEmptied", "PeerIdentityDuplicated", "TooManyDecisions" }
            .Select(r => $"DataSync_Notify_PauseReason_{r}"),
        ..DataSyncKindIds.All.Select(k => $"DataSync_Kind_{k}"),
    ];

    [TestMethod]
    public void Every_text_exists_in_both_languages_with_the_same_placeholders()
    {
        var en = Read("SharedResource.resx");
        var cn = Read("SharedResource.zh-Hans.resx");
        foreach (var key in Keys)
        {
            Assert.IsTrue(en.TryGetValue(key, out var english), $"{key} is missing in English");
            Assert.IsTrue(cn.TryGetValue(key, out var chinese), $"{key} is missing in Chinese");
            CollectionAssert.AreEquivalent(Placeholders(english), Placeholders(chinese), key);
            foreach (var text in new[] { english, chinese })
            {
                Assert.IsFalse(QuotedPlaceholder().IsMatch(text), $"{key} quotes a name: {text}");
                StringAssert.DoesNotMatch(text, new Regex("配置同步|配置包"), key);
            }
        }
    }

    [TestMethod]
    public void The_notifier_uses_only_texts_that_exist()
    {
        var en = Read("SharedResource.resx");
        foreach (var @case in new[] { "NeedsYou", "FollowOverride", "Paused", "Restore", "ReviewReady", "FirstSync",
                     "Attention", "NewReader" })
        {
            Assert.IsTrue(en.ContainsKey($"DataSync_Notify_{@case}_Title"), @case);
        }

        // Every case the notifier writes into a payload has its texts.
        var cases = typeof(DataSyncNotifier).GetFields()
            .Where(f => f.IsLiteral && f.Name.EndsWith("Case", StringComparison.Ordinal))
            .Select(f => (string) f.GetRawConstantValue()!)
            .ToList();
        Assert.AreEqual(8, cases.Count);
        foreach (var @case in cases)
            Assert.IsTrue(en.ContainsKey($"DataSync_Notify_{char.ToUpperInvariant(@case[0])}{@case[1..]}_Title"), @case);
    }

    private static List<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Value).Distinct().OrderBy(p => p).ToList();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"[«“「""']\{\d+\}[»”」""']")]
    private static partial Regex QuotedPlaceholder();

    private static Dictionary<string, string> Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "legacy", "Bakabase.InsideWorld.Business", "Resources", file);
            if (!File.Exists(path))
                path = Path.Combine(directory.FullName, "legacy", "Bakabase.InsideWorld.Business", "Resources", file);
            if (File.Exists(path))
            {
                return XDocument.Load(path).Root!.Elements("data")
                    .ToDictionary(d => (string) d.Attribute("name")!, d => (string?) d.Element("value") ?? "");
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException($"{file} was not found above {AppContext.BaseDirectory}.");
    }
}
