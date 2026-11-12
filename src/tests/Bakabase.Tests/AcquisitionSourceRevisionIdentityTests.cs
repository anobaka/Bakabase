using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionSourceRevisionIdentityTests
{
    [TestMethod]
    public void Reviewed_existing_short_link_mirror_can_freeze_its_expanded_source_without_losing_provenance()
    {
        var reviewed = new AcquisitionPlanSource {Id = "mirror", Url = "https://short.example/link"};
        var original = new AcquisitionPlanSource {Id = "original", Url = "https://example.org/old.zip"};
        var plan = new AcquisitionPlan {Title = "Review", Parts = [new() {Id = "part", Sources = [original, reviewed], SelectedSourceId = original.Id}]};
        var resolved = reviewed with {Url = "https://pan.baidu.com/s/fixture", ProviderId = "baidu", MethodId = "baidu.pcs",
            OriginalUrl = reviewed.Url, ResolutionChain = [reviewed.Url, "https://pan.baidu.com/s/fixture"]};
        var result = AcquisitionSourceRevisionService.RevisedPlan(plan, reviewed, resolved);
        Assert.AreEqual("mirror", result.Parts.Single().SelectedSourceId);
        Assert.AreEqual(resolved, result.Parts.Single().Sources.Single(s => s.Id == "mirror"));
        Assert.AreEqual(reviewed.Url, plan.Parts.Single().Sources.Single(s => s.Id == "mirror").Url);
        Assert.IsTrue(AcquisitionPlanCompiler.Compile(result).Nodes.Any(n => n.Kind == "baidu.transfer"));
        Assert.ThrowsExactly<ArgumentException>(() => AcquisitionSourceRevisionService.RevisedPlan(plan,
            reviewed with {Url = "https://unreviewed.example/link"}, resolved));
        Assert.ThrowsExactly<ArgumentException>(() => AcquisitionSourceRevisionService.RevisedPlan(plan, reviewed,
            resolved with {Id = "original"}));
    }

    [DataTestMethod]
    [DataRow("same", true)]
    [DataRow("accountVersion", false)]
    [DataRow("methodScope", false)]
    [DataRow("methodId", false)]
    [DataRow("accountOverride", false)]
    [DataRow("directory", false)]
    [DataRow("nonce", false)]
    [DataRow("url", false)]
    [DataRow("missingProof", false)]
    public void Starting_source_revision_requires_the_actual_reviewed_execution_identity(string change, bool accepted)
    {
        var source = new CloudSource {Id = "mirror", ProviderId = "oneDrive", Kind = "remote", Url = "/collection",
            MethodId = "oneDrive.rclone", AccountId = "account", AccountVersion = "generation-1", MethodScope = "rclone.v1"};
        var saved = new AcquisitionPlanSource {Id = source.Id, ProviderId = source.ProviderId, Kind = source.Kind,
            Url = source.Url, MethodId = source.MethodId, AccountId = source.AccountId};
        var target = new CloudDownloadTarget {Id = "part", SelectedSourceId = source.Id, Sources = [source], AccountId = source.AccountId};
        var plan = new AcquisitionPlan {Title = "Review", OutputDirectory = Path.GetTempPath(), Parts = [new()
            {Id = target.Id, SelectedSourceId = saved.Id, Sources = [saved]}]};
        var proof = new AcquisitionPlanSourceRevisionEvidence("decision", target.Id, "previous", source.Id, "mirror", "evidence",
            change == "missingProof" ? null : AcquisitionSourceRevisionService.SelectedExecutionIdentity(target, plan.OutputDirectory));
        var revision = new AcquisitionPlanRevision(2, plan, [], DateTime.UtcNow, AcquisitionSourceRevisionService.RevisionReason, 123, null, proof);
        var changedSource = change switch
        {
            "accountVersion" => source with {AccountVersion = "generation-2"},
            "methodScope" => source with {MethodScope = "rclone.v2"},
            "methodId" => source with {MethodId = "different-engine"},
            "url" => source with {Url = "/different-collection"},
            _ => source
        };
        target = target with {Sources = [changedSource], AccountId = change == "accountOverride" ? "other-account" : target.AccountId,
            AcquisitionNonce = change == "nonce" ? "different-generation" : null};
        var workset = new DownloadWorkset {OutputDirectory = change == "directory" ? Path.Combine(plan.OutputDirectory, "changed") : plan.OutputDirectory,
            Targets = [target]};
        Assert.AreEqual(accepted, AcquisitionPlanService.MatchesSourceRevisionExecution(plan, revision, workset));
    }
}
