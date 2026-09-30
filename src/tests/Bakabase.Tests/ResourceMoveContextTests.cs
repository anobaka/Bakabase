using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ResourceMoveContextTests
{
    [DataTestMethod]
    [DataRow("missing", "sourceContextRequired")]
    [DataRow("foreign", "foreignMoveSource")]
    [DataRow("foreignLegacy", "foreignMoveSource")]
    [DataRow("stale", "sourceContextChanged")]
    [DataRow("missingId", "invalidMoveSourceReferences")]
    [DataRow("extraId", "invalidMoveSourceReferences")]
    [DataRow("duplicate", "invalidMoveSourceReferences")]
    [DataRow("null", "invalidMoveSourceReferences")]
    public async Task InvalidOwnerReferencesNeverReachLocalPreviewOrMove(string scenario, string reason)
    {
        var service = new RecordingMoveService();
        var controller = new ResourceMoveController(service, new Identity());
        var input = LocalRequest();
        switch (scenario)
        {
            case "missing": input.ResourceRefs = null; break;
            case "foreign":
            case "foreignLegacy":
                input.ResourceRefs![1] = new("other-computer", "epoch", 2);
                if (scenario == "foreignLegacy") input.Origin = null;
                break;
            case "stale": input.ResourceRefs![1] = new("this-library", "old-epoch", 2); break;
            case "missingId": input.ResourceRefs = [new("this-library", "epoch", 1)]; break;
            case "extraId": input.ResourceRefs![1] = new("this-library", "epoch", 3); break;
            case "duplicate": input.ResourceRefs![1] = input.ResourceRefs[0]; break;
            case "null": input.ResourceRefs![1] = null!; break;
        }

        var preview = await controller.Preview(input);
        var create = await controller.CreateBatch(input);

        Assert.AreNotEqual(0, preview.Code);
        Assert.AreEqual(reason, preview.Message);
        Assert.AreNotEqual(0, create.Code);
        Assert.AreEqual(reason, create.Message);
        Assert.AreEqual(0, service.PreviewCalls);
        Assert.AreEqual(0, service.CreateCalls);
    }

    [TestMethod]
    public async Task CurrentLibraryReferencesPreserveTheLocalRequestRegardlessOfOrdering()
    {
        var service = new RecordingMoveService();
        var controller = new ResourceMoveController(service, new Identity());
        var input = LocalRequest();
        input.ResourceRefs = input.ResourceRefs!.Reverse().ToArray();

        Assert.AreEqual(0, (await controller.Preview(input)).Code);
        Assert.AreEqual(0, (await controller.CreateBatch(input)).Code);
        Assert.AreEqual(1, service.PreviewCalls);
        Assert.AreEqual(1, service.CreateCalls);
        CollectionAssert.AreEqual(input.ResourceIds, service.LastResourceIds);
        Assert.AreEqual(input.DestDir, service.LastDestination);
        Assert.AreSame(input, service.LastOptions);
    }

    [TestMethod]
    public async Task LegacyLocalIdsRemainCompatibleWithoutInitializingFederationIdentity()
    {
        var identity = new Identity();
        var service = new RecordingMoveService();
        var controller = new ResourceMoveController(service, identity);
        var input = LocalRequest();
        input.Origin = null;
        input.ResourceRefs = null;

        Assert.AreEqual(0, (await controller.Preview(input)).Code);
        Assert.AreEqual(0, (await controller.CreateBatch(input)).Code);
        Assert.AreEqual(0, identity.Reads);
        Assert.AreEqual(1, service.CreateCalls);
    }

    [TestMethod]
    public async Task ContextUsesFederationIdentityAndReadsTheCurrentEpoch()
    {
        var identity = new Identity();
        var controller = new ResourceMoveController(new RecordingMoveService(), identity);
        var first = (await controller.GetContext()).Data!;
        Assert.AreEqual("this-library", first.NodeId);
        Assert.AreEqual("epoch", first.LibraryEpoch);
        identity.Current = identity.Current with { LibraryEpoch = "new-epoch" };
        Assert.AreEqual("new-epoch", (await controller.GetContext()).Data!.LibraryEpoch);
    }

    [TestMethod]
    public async Task EpochChangedAfterPreviewRejectsCreateInsteadOfMovingTheSameNumberedResource()
    {
        var identity = new Identity();
        var service = new RecordingMoveService();
        var controller = new ResourceMoveController(service, identity);
        var input = LocalRequest();
        Assert.AreEqual(0, (await controller.Preview(input)).Code);
        identity.Current = identity.Current with { LibraryEpoch = "new-epoch" };

        var response = await controller.CreateBatch(input);

        Assert.AreEqual("sourceContextChanged", response.Message);
        Assert.AreEqual(0, service.CreateCalls);
    }

    private static ResourceMoveInputModel LocalRequest() => new()
    {
        ResourceIds = [1, 2], DestDir = "/local-target", Origin = "move-panel",
        ResourceRefs = [new("this-library", "epoch", 1), new("this-library", "epoch", 2)]
    };

    private sealed class Identity : INodeIdentityProvider
    {
        public NodeIdentity Current = new("this-library", "epoch", "This service");
        public int Reads;
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Current);
        }
    }

    private sealed class RecordingMoveService : IResourceMoveService
    {
        public int PreviewCalls;
        public int CreateCalls;
        public int[]? LastResourceIds;
        public string? LastDestination;
        public ResourceMoveRequestOptions? LastOptions;
        public Task<SingletonResponse<ResourceMoveBatchViewModel>> CreateBatch(int[] resourceIds, string destDir,
            ResourceMoveRequestOptions? options = null)
        {
            CreateCalls++;
            LastResourceIds = resourceIds;
            LastDestination = destDir;
            LastOptions = options;
            return Task.FromResult(new SingletonResponse<ResourceMoveBatchViewModel>(data: new("batch", 0)));
        }
        public Task<SingletonResponse<ResourceMovePreviewViewModel>> Preview(int[] resourceIds, string destDir)
        {
            PreviewCalls++;
            return Task.FromResult(new SingletonResponse<ResourceMovePreviewViewModel>(new ResourceMovePreviewViewModel()));
        }
        public Task<List<ResourceMoveBatchDetailViewModel>> GetBatches(string? origin = null, string? sourceTabId = null,
            bool activeOnly = false, int skip = 0, int take = 100) => throw new NotSupportedException();
        public Task<ResourceMoveBatchDetailViewModel?> GetBatch(string batchId) => throw new NotSupportedException();
        public Task<BaseResponse> CancelBatch(string batchId) => throw new NotSupportedException();
        public Task<BaseResponse> RetryBatch(string batchId) => throw new NotSupportedException();
        public Task<BaseResponse> ResolveConflict(int recordId, ResourceMoveConflictResolution resolution) => throw new NotSupportedException();
        public Task ApplyPanelPolicy() => throw new NotSupportedException();
        public Task<List<ResourceMoveRecordDbModel>> GetRecords(int maxCount = 100) => throw new NotSupportedException();
        public Task<BaseResponse> Retry(int recordId) => throw new NotSupportedException();
        public Task<BaseResponse> DeleteRecord(int recordId) => throw new NotSupportedException();
        public Task<BaseResponse> DeleteInactiveRecords() => throw new NotSupportedException();
        public Task ExecuteBatch(string batchId, BTaskArgs args) => throw new NotSupportedException();
        public Task MarkInterruptedOnStartup() => throw new NotSupportedException();
    }
}
