using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public class FileArtifactRetentionMetadataTests
{
    [TestMethod]
    [DataRow("complete", true)]
    [DataRow("broken-ignored-value", false)]
    [DataRow("broken-policy", false)]
    [DataRow("broken-escape", false)]
    [DataRow("broken-unicode-escape", false)]
    [DataRow("unescaped-control", false)]
    [DataRow("trailing-input", false)]
    [DataRow("malformed-plan-text", false)]
    public void Large_metadata_checks_the_complete_json_and_extracts_only_the_last_top_level_policy(string variant, bool valid)
    {
        var ignored = JsonSerializer.Serialize(string.Concat(Enumerable.Repeat("上一层 contains {\"steps\": []} \\n🚀 ", 40000)));
        ignored = variant switch
        {
            "broken-escape" => ignored[..^1] + "\\q\"",
            "broken-unicode-escape" => ignored[..^1] + "\\u7z00\"",
            "unescaped-control" => ignored[..^1] + "\t\"",
            _ => ignored
        };
        var tail = variant switch
        {
            "broken-ignored-value" => ",\"unrelated\":[1,]}",
            "broken-policy" => ",\"StoragePolicy\":false}",
            "trailing-input" => "}{}",
            "malformed-plan-text" => ""","planJson":"\uD800"}""",
            _ => "}"
        };
        // Both ignored contents and an escaped, differently cased top-level key cross read pages.
        var json = """{"storagePolicy":{"sourceCleanup":"keep"},"inputMetadataJson":""" + ignored +
            ""","unrelated":{"planJson":"bad","storagePolicy":{"sourceCleanup":"keep"}},"pLaNJson":"{}","STORAGE\u0050OLICY":{"sourceCleanup":"delete"}""" + tail;
        Assert.IsTrue(json.Length > 1024 * 1024);
        Assert.AreEqual(valid, FileArtifactRetentionMetadata.TryRead(json, out var result));
        if (valid)
        {
            Assert.AreEqual("delete", result!.StoragePolicy!.SourceCleanup);
            Assert.IsNotNull(result.ReadPlan());
        }
    }
}
