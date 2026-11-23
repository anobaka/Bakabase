using System.Data.Common;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    [TestMethod]
    public async Task Healthy_delivery_input_is_verified_once_while_pinned()
    {
        var artifact = await Register(["readme.txt"]);
        var observer = new VerificationObserver();
        var result = await AssessWithObservedStoreAsync(artifact, observer);

        Assert.IsNull(result.Suspension);
        CollectionAssert.AreEqual(new[] {"pin", "verify", "pin"}, observer.Sequence);
        var pins = await DeliveryPinsAsync(artifact);
        Assert.AreEqual(2, pins.Count);
        Assert.IsNull(pins.Single(p => p.ConsumerKey == $"workflow:12:delivery-input:{artifact.Id}").ReleasedAt);
        Assert.IsNotNull(pins.Single(p => p.ConsumerKey.Contains(":verification:", StringComparison.Ordinal)).ReleasedAt);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Change_during_delivery_verification_releases_only_its_own_pin(bool alreadyPinned)
    {
        var artifact = await Register(["readme.txt"]);
        var durableConsumer = $"workflow:12:delivery-input:{artifact.Id}";
        if (alreadyPinned)
            Assert.IsTrue(await store.PinConsumerAsync(artifact.OperationId, durableConsumer, "workflow", "12"));
        var observer = new VerificationObserver
        {
            AfterFirstPin = () => File.WriteAllTextAsync(artifact.Files.Single(), "changed")
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AssessWithObservedStoreAsync(artifact, observer));
        Assert.AreEqual("FILE_DELIVERY_INPUT_CHANGED", error.Message);
        CollectionAssert.AreEqual(new[] {"pin", "verify"}, observer.Sequence);
        var pins = await DeliveryPinsAsync(artifact);
        Assert.AreEqual(alreadyPinned ? 2 : 1, pins.Count);
        Assert.IsNotNull(pins.Single(p => p.ConsumerKey.Contains(":verification:", StringComparison.Ordinal)).ReleasedAt);
        if (alreadyPinned)
            Assert.IsNull(pins.Single(p => p.ConsumerKey == durableConsumer).ReleasedAt);
        Assert.AreEqual(ArtifactLocationState.Modified, (await store.GetArtifactAsync(artifact.Id))!.State);
    }

    [DataTestMethod]
    [DataRow(ArtifactLocationState.StorageUnavailable)]
    [DataRow(ArtifactLocationState.Missing)]
    [DataRow(ArtifactLocationState.Modified)]
    public async Task Restored_delivery_input_keeps_the_existing_revalidation_path(ArtifactLocationState state)
    {
        var artifact = await Register(["readme.txt"]);
        await db.Set<ArtifactDbModel>().Where(a => a.Id == artifact.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, state));
        var observer = new VerificationObserver();

        var result = await AssessWithObservedStoreAsync(artifact, observer);

        Assert.IsNull(result.Suspension);
        CollectionAssert.AreEqual(new[] {"verify", "pin", "verify"}, observer.Sequence);
        Assert.IsNull((await DeliveryPinsAsync(artifact)).Single().ReleasedAt);
        Assert.AreEqual(ArtifactLocationState.Available, (await store.GetArtifactAsync(artifact.Id))!.State);
    }

    [TestMethod]
    public async Task Accepted_delivery_input_remains_verified_without_new_pins()
    {
        var artifact = await Register(["readme.txt"]);
        var observer = new VerificationObserver();
        var result = await AssessWithObservedStoreAsync(artifact, observer, accepted: true);

        Assert.IsNull(result.Suspension);
        CollectionAssert.AreEqual(new[] {"verify"}, observer.Sequence);
        Assert.AreEqual(0, (await DeliveryPinsAsync(artifact)).Count);
    }

    private Task<List<ArtifactConsumerDbModel>> DeliveryPinsAsync(ArtifactRecord artifact) =>
        db.Set<ArtifactConsumerDbModel>().AsNoTracking()
            .Where(p => p.OperationId == artifact.OperationId && p.OwnerKind == "workflow" && p.OwnerId == "12")
            .ToListAsync();

    private async Task<WorkflowItemOutcome> AssessWithObservedStoreAsync(ArtifactRecord artifact,
        VerificationObserver observer, bool accepted = false)
    {
        var options = new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(db.Database.GetDbConnection().ConnectionString).AddInterceptors(observer).Options;
        await using var provider = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => appData))
            .BuildServiceProvider();
        var item = Workset(artifact);
        if (accepted)
            item = item with {Targets = [item.Targets.Single() with
                {DeliveryReview = new(artifact.Id, artifact.Fingerprint, Accepted: true)}]};
        return await activity.ProcessItemAsync(Context(item, serviceProvider: provider), item, default);
    }

    private sealed class VerificationObserver : DbCommandInterceptor
    {
        internal List<string> Sequence { get; } = [];
        internal Func<Task>? AfterFirstPin { get; init; }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Artifacts\"", StringComparison.Ordinal))
                Sequence.Add("verify");
            else if (command.CommandText.StartsWith("UPDATE \"ArtifactConsumers\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"OwnerKind\"", StringComparison.Ordinal))
            {
                var first = !Sequence.Contains("pin");
                Sequence.Add("pin");
                if (first && AfterFirstPin != null) await AfterFirstPin();
            }
            return result;
        }
    }
}
