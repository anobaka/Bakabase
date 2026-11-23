using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactPublisherTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Same_run_resumes_partial_publication_and_verifies_existing_files(bool changePublishedFile)
    {
        var input = await Input();
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running};
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync();
        var consumer = $"workflow:{run.Id}:publish:files";
        var binding = Guid.NewGuid().ToString("N");
        var policy = new ArtifactDeliveryPolicy();
        var scopes = _services.GetRequiredService<IServiceScopeFactory>();
        using var cancellation = new CancellationTokenSource();
        var copies = 0;
        var interrupted = new FileArtifactPublisher(_store, new Storage(), () => _appData, scopes,
            async (source, destination, ct) =>
            {
                if (++copies == 2)
                {
                    await destination.WriteAsync(new byte[] {42}, ct);
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                }
                await source.CopyToAsync(destination, ct);
            });
        await Assert.ThrowsAsync<OperationCanceledException>(() => interrupted.PublishAsync("processing-intent", input,
            _destination, consumer, run.Id, cancellation.Token, deliveryPolicy: policy, currentStageDirectoryBindingId: binding));

        var operation = await _db.ArtifactOperations.AsNoTracking().SingleAsync(o => o.Kind == "files.publish");
        var committed = input.Manifest.Entries.Select(e => Path.Combine(_destination, e.RelativePath)).Single(File.Exists);
        var stage = Directory.EnumerateDirectories(_destination, ".bakabase-publish-*").Single();
        var partial = Directory.EnumerateFiles(Path.Combine(stage, "files"), "*", SearchOption.AllDirectories).Single();
        Assert.AreEqual(1L, new FileInfo(partial).Length);
        if (changePublishedFile)
            await File.WriteAllBytesAsync(committed, Enumerable.Repeat((byte)42, (int)new FileInfo(committed).Length).ToArray());
        var identity = ArtifactFileIdentity.Read(committed);
        var timestamp = File.GetLastWriteTimeUtc(committed);
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(committed));

        var retriedCopies = 0;
        var retry = new FileArtifactPublisher(_store, new Storage(), () => _appData, scopes,
            async (source, destination, ct) =>
            {
                retriedCopies++;
                await source.CopyToAsync(destination, ct);
            });
        var result = await retry.PublishAsync("processing-intent", input, _destination, consumer, run.Id,
            deliveryPolicy: policy, currentStageDirectoryBindingId: binding);
        Assert.AreEqual(identity, ArtifactFileIdentity.Read(committed));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(committed));
        CollectionAssert.AreEqual(hash, SHA256.HashData(await File.ReadAllBytesAsync(committed)));
        if (changePublishedFile)
        {
            Assert.IsNull(result.Artifact);
            Assert.AreEqual("PublicationConflict", result.WaitReason);
            Assert.AreEqual(0, retriedCopies);
            return;
        }
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual(operation.Id, result.Artifact.OperationId);
        Assert.AreEqual(operation.Attempt, (await _store.GetOperationAsync(operation.Id))!.Attempt);
        Assert.AreEqual(1, retriedCopies);
        Assert.IsTrue((await ArtifactManifestFiles.ValidateAsync(_destination, input.Manifest)).Available);
        Assert.IsFalse(Directory.Exists(stage));
    }
}
