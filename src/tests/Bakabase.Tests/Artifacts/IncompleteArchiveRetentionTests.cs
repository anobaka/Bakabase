using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class IncompleteArchiveRetentionTests
{
    [TestMethod]
    public async Task Legacy_wait_previews_only_group_receipts_then_keeps_one_failed_group_across_restarts()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "incomplete-group-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
            foreach (var name in new[]{"a.zip","b.zip","c.zip","d.zip","note.pdf"}) await File.WriteAllTextAsync(Path.Combine(input,name),name);
            var files = Directory.GetFiles(input);
            var manifest = await ArtifactManifestFiles.CaptureAsync(input,files,CancellationToken.None);
            var record = new ArtifactRecord("input-id","download-id","download",input,files,manifest,manifest.Fingerprint,
                ArtifactLocationState.Available,true,[],null);
            var plan = new FileProcessingPlan {Requirement="required",Steps=[new(){Id="unpack",Op="extractArchive"}]};
            var stateDirectory = Path.Combine(root, "state");
            var archives = new Archives();
            var scope = new ArchiveGroupExecution(new(23,"frozen-part","unpack"),record.Id,record.Fingerprint,"operation",1,null);
            Task<FileProcessingPlanResult> Run(ArchiveGroupExecution? group) => new FileProcessingPlanExecutor(archives).ExecuteAsync(
                plan,input,files,stateDirectory,stopAfterStepId:"unpack",snapshotSource:new(record,_=>Task.FromResult(manifest)){ArchiveGroup=group});
            Assert.AreEqual("MissingVolumes",(await Run(null)).FailureCode);
            var path = Directory.GetFiles(stateDirectory,"state.json",SearchOption.AllDirectories).Single();
            var directory = Path.GetDirectoryName(path)!;
            var saved = await File.ReadAllBytesAsync(path);
            var first = await FileProcessingPlanExecutor.InspectPendingGroupAsync(directory,record,scope,default);
            Assert.IsNotNull(first);
            CollectionAssert.AreEqual(new[]{"b.zip"},first.Members.ToArray());
            Assert.AreEqual(1,first.CompletedGroupCount);
            Assert.AreEqual(5L,first.TotalBytes);
            Assert.AreEqual(2,archives.Probes);
            CollectionAssert.AreEqual(saved,await File.ReadAllBytesAsync(path));
            foreach (var other in new[]{scope with {Attempt=2},scope with {Owner=scope.Owner with {RunId=33}},
                         scope with {Owner=scope.Owner with {TargetId="another"}}})
                Assert.AreNotEqual(first.Token,(await FileProcessingPlanExecutor.InspectPendingGroupAsync(directory,record,other,default))?.Token);
            await Assert.ThrowsExactlyAsync<FileProcessingInputChangedException>(()=>Run(scope with {RequestedToken=new string('0',64)}));
            var second = await Run(scope with {RequestedToken=first.Token});
            Assert.AreEqual("MissingVolumes",second.FailureCode,second.Message);
            Assert.IsNotNull(second.IncompleteArchiveGroup);
            CollectionAssert.AreEqual(new[]{"d.zip"},second.IncompleteArchiveGroup.Members.ToArray());
            Assert.AreEqual(2,second.IncompleteArchiveGroup.CompletedGroupCount);
            CollectionAssert.AreEqual(new[]{"a.zip","c.zip"},archives.Extracted);
            var result = await Run(scope with {RequestedToken=second.IncompleteArchiveGroup.Token});
            Assert.IsTrue(result.Completed,result.Message);
            CollectionAssert.AreEquivalent(new[]{"a.zip.txt","b.zip","c.zip.txt","d.zip","note.pdf"},result.Files.Select(Path.GetFileName).ToArray());
            CollectionAssert.AreEquivalent(new[]{"b.zip","d.zip"},result.RetainedArchiveFiles!.ToArray());
            foreach(var original in new[]{"b.zip","d.zip","note.pdf"}) Assert.AreEqual(original,await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory!,original)));
            CollectionAssert.AreEqual(new[]{"a.zip","c.zip"},archives.Extracted);
            Assert.IsTrue(result.FileLineage!.Entries.Where(e=>e.InputRelativePath is "b.zip" or "d.zip").All(e=>!e.Replaced));
            var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.AreEqual(2,json.RootElement.GetProperty("operations").EnumerateObject().Count(o=>
                !o.Value.GetProperty("completed").GetBoolean() && o.Value.GetProperty("retainedGroupToken").ValueKind==JsonValueKind.String));
        }
        finally {Directory.Delete(root,true);}
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public List<string> Extracted {get;}=[];
        public int Probes {get;private set;}
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file,IReadOnlyList<string?> candidates,Action<string>? output,CancellationToken ct)
        {
            Probes++;
            var missing=Path.GetFileName(file) is "b.zip" or "d.zip";
            return Task.FromResult(new ArchivePasswordProbe(!missing,null,[],"",""){FailureCode=missing?"MissingVolumes":null});
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,Action<int>? progress,CancellationToken ct)
        {
            var name=Path.GetFileName(request.Files[0]);Extracted.Add(name);
            await File.WriteAllTextAsync(Path.Combine(request.Directory,name+".txt"),name,ct);
            return new(true,request.Directory,"");
        }
    }
}
