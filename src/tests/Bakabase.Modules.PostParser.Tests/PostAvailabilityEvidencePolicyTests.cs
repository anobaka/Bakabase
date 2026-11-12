using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public sealed class PostAvailabilityEvidencePolicyTests
{
    [TestMethod]
    [DataRow("expired", "老了")]
    [DataRow("expired", "这个帖子太老了")]
    [DataRow("expired", "链接没失效")]
    [DataRow("expired", "链接没过期")]
    [DataRow("expired", "链接不是不存在")]
    [DataRow("expired", "链接失效了吗")]
    [DataRow("expired", "链接失效了？")]
    [DataRow("expired", "可能链接失效了")]
    [DataRow("expired", "有人说“链接失效了”")]
    [DataRow("expired", "the link is not expired")]
    [DataRow("expired", "has it expired")]
    [DataRow("restored", "链接从未补档")]
    [DataRow("restored", "链接尚未补档")]
    [DataRow("restored", "请重新上传文件")]
    [DataRow("restored", "希望已经补档了")]
    [DataRow("restored", "not reuploaded")]
    [DataRow("restored", "稍后重新上传文件")]
    [DataRow("restored", "will be reuploaded")]
    [DataRow("restored", "重新上传一下")]
    [DataRow("restored", "this needs to be reuploaded")]
    public void AmbiguousNegatedAndRequestedStatementsDoNotProveAvailability(string status, string evidence)
    {
        var raw = new PostAvailabilityAssessment {Status = status, Evidence = [evidence], Reason = "model inference"};
        var effective = PostAvailabilityEvidencePolicy.Apply(raw)!;
        Assert.AreEqual("unknown", effective.Status);
        Assert.AreEqual(PostAvailabilityEvidencePolicy.InsufficientEvidence, effective.Reason);
        Assert.AreEqual(status, raw.Status, "Projection must preserve the saved assessment.");
        CollectionAssert.AreEqual(raw.Evidence, effective.Evidence);
    }

    [TestMethod]
    [DataRow("expired", "下载链接失效了")]
    [DataRow("expired", "分享已被取消")]
    [DataRow("expired", "无法下载")]
    [DataRow("expired", "不能下载")]
    [DataRow("expired", "the download link is broken")]
    [DataRow("expired", "file not found")]
    [DataRow("expired", "链接失效了，求补档")]
    [DataRow("restored", "已经补档了")]
    [DataRow("restored", "文件重新上传了")]
    [DataRow("restored", "reuploaded")]
    public void ExplicitCompletedStatementsRemainUsable(string status, string evidence) =>
        Assert.AreEqual(status, PostAvailabilityEvidencePolicy.EffectiveStatus(status, [evidence]));
}
