using System.Reflection;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionNativeStoragePolicyTests
{
    [TestMethod]
    public async Task HttpNamingUsesNewPolicyButExistingGenerationKeepsItsPhysicalPath()
    {
        var fake = DispatchProxy.Create<IHttpDownloadManager, HttpProxy>(); var state = (HttpProxy)(object)fake;
        using var services = new ServiceCollection().AddSingleton(fake).BuildServiceProvider();
        var target = new CloudDownloadTarget {Id = "source", Title = "Readable title", SelectedSourceId = "source",
            Sources = [new() {Id = "source", ProviderId = "http", Kind = "http", Url = "https://example.invalid/file.zip"}], StoragePolicy = new()};
        var item = new DownloadWorkset {Title = "Task", OutputDirectory = Path.Combine(Path.GetTempPath(), "native-policy"), Targets = [target]};
        var ctx = new WorkflowExecutionContext {RunId = 8, WorkflowDefinitionId = 0, TriggerKind = "test", Payload = item,
            ActivityConfigJson = "{}", Services = services, Logger = NullLogger.Instance};
        await CloudActivity.QueueNative(ctx, item, target, default);
        StringAssert.Contains(state.Request!.OutputDirectory, "Readable title [D");
        var directory = state.Request.OutputDirectory; var key = state.Request.IdempotencyKey;
        state.Existing = new() {Id = "original", Name = "Old", OutputDirectory = directory};
        await CloudActivity.QueueNative(ctx, item, target with {StoragePolicy = new() {DownloadDirectoryTemplate = "changed/{OperationId}"}}, default);
        Assert.AreEqual(directory, state.Request.OutputDirectory); Assert.AreEqual(key, state.Request.IdempotencyKey);
        Assert.AreEqual(0, state.ResumeCalls);
    }
    public class HttpProxy : DispatchProxy
    {
        public HttpDownloadRequest? Request; public HttpDownloadJob? Existing; public int ResumeCalls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IHttpDownloadManager.FindByIdempotencyKeyAsync)) return Task.FromResult(Existing);
            if (method.Name == nameof(IHttpDownloadManager.EnsureQueuedAsync))
            {
                Request = (HttpDownloadRequest)args![0]!;
                return Task.FromResult(Existing ?? new HttpDownloadJob {Id = "new", Name = "New", OutputDirectory = Request.OutputDirectory});
            }
            if (method.Name == nameof(IHttpDownloadManager.ResumeAsync)) ResumeCalls++;
            throw new AssertFailedException("Unexpected transport action: " + method.Name);
        }
    }
}
