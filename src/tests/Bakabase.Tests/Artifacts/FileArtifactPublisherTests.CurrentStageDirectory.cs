using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactPublisherTests
{
    [TestMethod]
    public async Task Explicit_current_delivery_binding_reuses_its_exact_registered_files_without_copying_or_adopting_siblings()
    {
        var input = await Input();
        var previous = (await Publisher.PublishAsync("old", input, _destination, "old-consumer")).Artifact!;
        var unrelated = Path.Combine(_destination, "keep-unregistered.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        var defaultResult = await Publisher.PublishAsync("current-input", previous, _destination, "default-consumer");
        Assert.AreEqual("PublicationConflict", defaultResult.WaitReason);
        var binding = Guid.NewGuid().ToString("N");
        var noCopies = new FileArtifactPublisher(_store, new Storage(), () => _appData,
            _services.GetRequiredService<IServiceScopeFactory>(), (_, _, _) => throw new AssertFailedException("The existing stage files were copied."));
        var result = await noCopies.PublishAsync("current-input", previous, _destination, "current-consumer",
            currentStageDirectoryBindingId: binding);
        Assert.IsNotNull(result.Artifact, result.Message);
        Assert.AreEqual(previous.Fingerprint, result.Artifact.Fingerprint);
        CollectionAssert.AreEquivalent(previous.Files.ToArray(), result.Artifact.Files.ToArray());
        Assert.AreEqual("keep", await File.ReadAllTextAsync(unrelated));
        Assert.IsFalse(result.Artifact.Files.Contains(unrelated));
        Assert.AreEqual(result.Artifact.Id, (await noCopies.ResolveAsync("current-input", _destination,
            currentStageDirectoryBindingId: binding))?.Id);
        Assert.IsNull(await noCopies.ResolveAsync("current-input", _destination));
        Assert.IsNull(await noCopies.ResolveAsync("current-input", _destination,
            currentStageDirectoryBindingId: Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public async Task Explicit_current_directory_merges_new_projection_without_deleting_old_layout_or_unknown_files()
    {
        var root = Path.Combine(_root, "current-input"); var file = Path.Combine(root, "wrapper", "one.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, "one");
        var input = await _store.AdoptAsync(new("stage-projection", "stage-projection", "process", "projection"),
            new(root, [file], "processedFiles", false), "input");
        Assert.IsNotNull((await Publisher.PublishAsync("old", input, _destination, "old")).Artifact);
        var extra = Path.Combine(_destination, "unknown.txt"); await File.WriteAllTextAsync(extra, "retain");
        var result = await Publisher.PublishAsync("new", input, _destination, "stage",
            deliveryPolicy: new() {CollapseSingleRootDirectory = true}, currentStageDirectoryBindingId: Guid.NewGuid().ToString("N"));
        Assert.IsNotNull(result.Artifact, result.Message);
        CollectionAssert.AreEqual(new[] {Path.Combine(_destination, "one.txt")}, result.Artifact.Files.ToArray());
        Assert.AreEqual("one", await File.ReadAllTextAsync(Path.Combine(_destination, "wrapper", "one.txt")));
        Assert.AreEqual("retain", await File.ReadAllTextAsync(extra));
    }

    [TestMethod]
    public async Task Explicit_current_directory_still_refuses_different_files_and_rejects_symlinks()
    {
        var input = await Input(); Directory.CreateDirectory(_destination);
        var conflict = Path.Combine(_destination, "one.txt"); await File.WriteAllTextAsync(conflict, "different");
        var result = await Publisher.PublishAsync("current", input, _destination, "stage",
            currentStageDirectoryBindingId: Guid.NewGuid().ToString("N"));
        Assert.IsNull(result.Artifact); Assert.AreEqual("PublicationConflict", result.WaitReason);
        Assert.AreEqual("different", await File.ReadAllTextAsync(conflict));
        Assert.IsTrue(input.Files.All(File.Exists));
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(conflict); File.CreateSymbolicLink(conflict, input.Files[0]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Publisher.PublishAsync("current-link", input, _destination, "stage",
                currentStageDirectoryBindingId: Guid.NewGuid().ToString("N")));
            Assert.IsNotNull(new FileInfo(conflict).LinkTarget);
            Assert.IsTrue(input.Files.All(File.Exists));
        }
    }

    [TestMethod]
    public void Current_stage_mode_requires_the_same_frozen_target_binding_and_path()
    {
        var id = Guid.NewGuid().ToString("N");
        var target = new CloudDownloadTarget {Id = "part", StageDirectories = new()
            {Delivery = new(_destination, "borrowed", "configured") {BindingId = id}}};
        var config = new FileArtifactPublicationConfig {StageDirectoryBindingIds = new() {{"part", id}}};
        Assert.AreEqual(id, PublishFileArtifactsActivity.CurrentStageBinding(target, config, _destination));
        Assert.IsNull(PublishFileArtifactsActivity.CurrentStageBinding(target, new(), _destination));
        Assert.ThrowsException<InvalidOperationException>(() => PublishFileArtifactsActivity.CurrentStageBinding(target, config, _destination + "-other"));
        Assert.ThrowsException<InvalidOperationException>(() => PublishFileArtifactsActivity.CurrentStageBinding(target with {StageDirectories = null}, config, _destination));
        Assert.ThrowsException<InvalidOperationException>(() => PublishFileArtifactsActivity.CurrentStageBinding(target,
            config with {StageDirectoryBindingIds = new() {{"part", Guid.NewGuid().ToString("N")}}}, _destination));
        Assert.ThrowsException<ArgumentException>(() => FileArtifactPublisher.Intent("input", _destination, currentStageDirectoryBindingId: "not-issued"));
    }
}
