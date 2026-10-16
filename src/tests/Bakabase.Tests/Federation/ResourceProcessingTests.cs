using System.Reflection;
using System.Security.Cryptography;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Components.Enhancers.Regex;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bakabase.Service.Components.Federation.Processing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Federation;

[TestClass]
[DoNotParallelize]
public sealed class ResourceProcessingTests
{
    private string _directory = null!;
    private BakabaseDbContext _db = null!;
    private ResourceProcessingJobStore _jobs = null!;
    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "bakabase-processing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _db = new(new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(_directory, "test.sqlite") + ";Pooling=False").Options);
        await _db.Database.EnsureCreatedAsync();
        _jobs = new(_db);
    }
    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        Directory.Delete(_directory, true);
    }
    private static ResourceProcessingInput Input(int id = 1) => new(Guid.NewGuid().ToString("N"), "owner", "epoch", id,
        null, new string('a', 64), ResourceProcessingKind.PlayableFiles, new() {Extensions = ["mp4"]});

    [TestMethod]
    public async Task Disk_queue_is_requester_scoped_idempotent_CAS_and_restart_resumable()
    {
        var input = Input();
        var first = await _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-a", input, "{}", default);
        Assert.AreEqual(first.Id, (await _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-a", input, "{}", default)).Id);
        Assert.IsNull(await _jobs.GetAsync(ResourceProcessingRole.Worker, "manager-b", input.JobId, default));
        var stale = (await _jobs.GetByIdAsync(first.Id, default))!;
        first.State = "Running"; first.Attempt++; first.ProcessedCount = 64;
        await _jobs.UpdateAsync(first, default);
        stale.State = "Cancelled";
        var conflict = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => _jobs.UpdateAsync(stale, default));
        Assert.AreEqual("JobChanged", conflict.Code);
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        var resumed = new ResourceProcessingJobStore(_db);
        Assert.AreEqual(1, await resumed.RecoverInterruptedAsync(ResourceProcessingRole.Worker, default));
        var next = await resumed.NextQueuedAsync(ResourceProcessingRole.Worker, default);
        Assert.AreEqual(64L, next!.ProcessedCount);
        Assert.AreEqual(1, next.Attempt);
        var journal = new MetadataSourceJournal(_db, new());
        Assert.AreEqual(0L, (await journal.HeadAsync(default)).Revision, "Job progress must not trigger metadata replication.");
    }

    [TestMethod]
    public async Task Batch_cursor_creation_is_atomic_and_list_does_not_materialize_payloads()
    {
        var input = Input();
        var row = await _jobs.CreateBatchAsync("local", input.JobId, input.OwnerId, input.OwnerEpoch,
            input.RuleHash, input.Kind, "{\"afterId\":256}", default);
        Assert.AreEqual(0, row.ResourceId);
        row.ResourceId = 23; row.State = "Running";
        await _jobs.UpdateAsync(row, default);
        Assert.AreEqual(23, (await _jobs.CreateBatchAsync("local", input.JobId, input.OwnerId, input.OwnerEpoch,
            input.RuleHash, input.Kind, "{}", default)).ResourceId);
        var page = await _jobs.ListAsync(ResourceProcessingRole.Manager, null, 1, default);
        Assert.AreEqual(1, page.Count);
        Assert.AreEqual(23, page[0].ResourceId);
        Assert.AreEqual(0, (await _jobs.ListAsync(ResourceProcessingRole.Manager, page[0].Id, 1, default)).Count);
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => _jobs.ListAsync(ResourceProcessingRole.Manager, null, 101, default));
    }

    [TestMethod]
    public async Task Artifact_chunks_resume_verify_hash_and_never_expose_worker_absolute_paths()
    {
        var artifacts = new ResourceProcessingArtifacts(new DataDirectory(_directory));
        var prepared = new ResourceProcessingPrepared(Input(), "token", 1, "catalog", "{}");
        var bytes = RandomNumberGenerator.GetBytes(ResourceProcessingWire.ArtifactChunkBytes + 7);
        var manifest = new ResourceProcessingArtifact(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "cover.jpg", bytes.Length);
        var first = Convert.ToBase64String(bytes.AsSpan(0, ResourceProcessingWire.ArtifactChunkBytes));
        Assert.AreEqual(ResourceProcessingWire.ArtifactChunkBytes, await artifacts.AppendOwnerChunkAsync(prepared, manifest, 0, first, default));
        Assert.AreEqual(ResourceProcessingWire.ArtifactChunkBytes, await artifacts.AppendOwnerChunkAsync(prepared, manifest, 0, first, default));
        await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => artifacts.AppendOwnerChunkAsync(prepared, manifest,
            0, Convert.ToBase64String(new byte[8]), default));
        Assert.AreEqual(bytes.Length, await artifacts.AppendOwnerChunkAsync(prepared, manifest, ResourceProcessingWire.ArtifactChunkBytes,
            Convert.ToBase64String(bytes.AsSpan(ResourceProcessingWire.ArtifactChunkBytes)), default));
        var wrong = prepared with {Token = "other-requester-token"};
        Assert.AreNotEqual(artifacts.GetDirectory(prepared, "owner"), artifacts.GetDirectory(wrong, "owner"));
        Assert.ThrowsException<ResourceProcessingException>(() => artifacts.OpenWorkerArtifact(prepared, "../secrets"));
    }

    [TestMethod]
    public async Task Worker_discovery_uses_forward_mapping_and_returns_only_resource_relative_paths()
    {
        var root = Path.Combine(_directory, "mapped");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(root, "movie.MP4"), "video");
        await File.WriteAllTextAsync(Path.Combine(root, "nested", "other.mp4"), "video");
        await File.WriteAllTextAsync(Path.Combine(root, "cover.jpg"), "image");
        var prepared = new ResourceProcessingPrepared(Input(), "token", 1, "catalog", ResourceProcessingWire.Serialize(
            new Resource {Id = 1, Path = @"Z:\Library\Collection", IsFile = false}));
        var managed = Stub<IManagedServerService>((method, _) => method.Name == "GetAsync"
            ? Task.FromResult(new ManagedServersView(true, [new("owner", "Source", "http://example.invalid", DateTime.UtcNow,
                null, [new(@"Z:\Library\Collection", root)], ManagedServerState.Online, null, null)], []))
            : throw new NotSupportedException());
        var worker = new ResourceProcessingWorker(new Identity("worker", "worker-epoch"), managed,
            new UserStoragePolicy(false), new(new DataDirectory(_directory)), new ServiceCollection().BuildServiceProvider(), null!);
        var phases = new List<string>();
        var output = await worker.ExecuteAsync(prepared, (p, _) => { phases.Add(p.Phase); return Task.CompletedTask; }, default);
        CollectionAssert.AreEquivalent(new[] {"movie.MP4", "nested/other.mp4"}, output.PlayableFiles.ToArray());
        Assert.IsFalse(ResourceProcessingWire.Serialize(output).Contains(root));
        Assert.AreEqual(0, await _db.ResourceCaches.CountAsync(), "A worker must not overwrite its own same-numbered resource cache.");
        var noMapping = prepared with {ResourceJson = ResourceProcessingWire.Serialize(new Resource {Id = 1, Path = @"Y:\Private"})};
        var rejected = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => worker.ExecuteAsync(noMapping, (_, _) => Task.CompletedTask, default));
        Assert.AreEqual("PathNotMapped", rejected.Code);
    }

    [TestMethod]
    public async Task Ordinary_discovery_keeps_extension_regex_semantics_and_honors_cancellation()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "first.MP4"), "");
        await File.WriteAllTextAsync(Path.Combine(_directory, "second.mp4"), "");
        var paths = await PlayableFileDiscovery.DiscoverAsync(_directory, false, new() {Extensions = ["MP4"], FileNamePattern = "first"}, default);
        Assert.AreEqual(1, paths.Length);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => PlayableFileDiscovery.DiscoverAsync(_directory, false,
            new() {Extensions = ["mp4"]}, cancelled.Token));
    }

    [TestMethod]
    public async Task Owner_applies_windows_paths_without_local_normalization_and_replays_receipt_after_restart()
    {
        var provider = await BuildOwnerServices();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>();
        db.ResourcesV2.Add(new ResourceDbModel {Id = 42, Path = @"Z:\Movies\Collection"});
        await db.SaveChangesAsync();
        var owner = sp.GetRequiredService<ResourceProcessingOwner>();
        var input = Input(42);
        var prepared = await owner.PrepareAsync("manager-a", input, default);
        var output = new ResourceProcessingOutput(ResourceProcessingKind.PlayableFiles, ["nested/movie.mp4"], [], [], 2);
        var receipt = await owner.ApplyAsync("manager-a", input.JobId, prepared.Token, output, default);
        Assert.AreEqual(@"Z:\Movies\Collection\nested\movie.mp4", (await db.ResourceCaches.AsNoTracking().SingleAsync()).PlayableFilePaths!.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString)!.Single());
        var replay = await owner.ApplyAsync("manager-a", input.JobId, prepared.Token, output, default);
        Assert.AreEqual(receipt, replay);
        var state = (await sp.GetRequiredService<ResourceProcessingJobStore>().GetAsync(ResourceProcessingRole.Owner, "manager-a", input.JobId, default))!;
        Assert.IsFalse(state.InputJson.Contains("ResourceJson"));
        Assert.IsNull(state.ResultJson);
        Assert.AreEqual(receipt, await owner.GetReceiptAsync("manager-a", input.JobId, prepared.Token, default));
        await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.ApplyAsync("manager-b", input.JobId, prepared.Token, output, default));
        await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.ApplyAsync("manager-a", input.JobId, prepared.Token,
            output with {PlayableFiles = ["different.mp4"]}, default));
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Changed_source_revision_conflicts_without_overwriting_cache()
    {
        var provider = await BuildOwnerServices();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>();
        db.ResourcesV2.Add(new ResourceDbModel {Id = 17, Path = "/source/old"}); await db.SaveChangesAsync();
        var owner = sp.GetRequiredService<ResourceProcessingOwner>();
        var input = Input(17);
        var prepared = await owner.PrepareAsync("manager-a", input, default);
        await db.Database.ExecuteSqlRawAsync("UPDATE ResourcesV2 SET Path='/source/new' WHERE Id=17");
        var error = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.ApplyAsync("manager-a", input.JobId,
            prepared.Token, new(ResourceProcessingKind.PlayableFiles, ["movie.mp4"], [], [], 1), default));
        Assert.AreEqual("ResourceChanged", error.Code);
        Assert.AreEqual(0, await db.ResourceCaches.CountAsync());
        Assert.IsNull(await owner.GetReceiptAsync("manager-a", input.JobId, prepared.Token, default));
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Real_regex_worker_computes_without_local_writes_and_owner_applies_pinned_mapping()
    {
        var provider = await BuildOwnerServices();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>();
        var path = Path.Combine(_directory, "Album"); Directory.CreateDirectory(path);
        db.ResourcesV2.Add(new ResourceDbModel {Id = 9, Path = path}); await db.SaveChangesAsync();
        var options = new EnhancerFullOptions {EnhancerId = (int)EnhancerId.Regex, Expressions = ["(?<title>Album)"],
            TargetOptions = [new() {Target = (int)RegexEnhancerTarget.CaptureGroups, DynamicTarget = "title",
                PropertyPool = PropertyPool.Reserved, PropertyId = (int)ReservedProperty.Name}]};
        var input = new ResourceProcessingInput(Guid.NewGuid().ToString("N"), "owner", "epoch", 9, null,
            new string('a', 64), ResourceProcessingKind.Enhancement, Enhancer: options);
        var owner = sp.GetRequiredService<ResourceProcessingOwner>();
        var prepared = await owner.PrepareAsync("manager-a", input, default);
        var worker = new ResourceProcessingWorker(new Identity("owner", "epoch"), null!, new UserStoragePolicy(false),
            sp.GetRequiredService<ResourceProcessingArtifacts>(), sp, sp.GetRequiredService<IEnhancerDescriptors>());
        var output = await worker.ExecuteAsync(prepared, (_, _) => Task.CompletedTask, default);
        Assert.AreEqual(1, output.Values.Count);
        Assert.AreEqual(0, await db.Enhancements.CountAsync(), "Pure computation must not persist worker metadata.");
        await owner.ApplyAsync("manager-a", input.JobId, prepared.Token, output, default);
        Assert.AreEqual(1, await db.Enhancements.CountAsync());
        var resource = (await sp.GetRequiredService<IResourceService>().Get(9,
            Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.Properties))!;
        Assert.IsTrue(resource.Properties![(int)PropertyPool.Reserved][(int)ReservedProperty.Name].Values!
            .Any(v => v.BizValue?.ToString() == "Album"), "Pinned source mapping must work without copying the manager profile into the owner.");
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Pending_payload_quota_includes_unacked_worker_results_and_releases_on_cleanup()
    {
        Bakabase.InsideWorld.Business.Models.Db.ResourceProcessingJobDbModel? first = null;
        for (var i = 0; i < 16; i++)
        {
            var row = await _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-a", Input(), "{\"prepared\":true}", default);
            first ??= row;
            row.State = "Completed"; row.ResultJson = "{\"result\":true}";
            await _jobs.UpdateAsync(row, default);
        }
        var error = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() =>
            _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-a", Input(), "{}", default));
        Assert.AreEqual("PendingJobLimit", error.Code);
        await _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-b", Input(), "{}", default);
        first!.InputJson = "{}"; first.ResultJson = null;
        await _jobs.UpdateAsync(first, default);
        await _jobs.CreateAsync(ResourceProcessingRole.Worker, "manager-a", Input(), "{}", default);
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
    }

    [TestMethod]
    public void Protocol_hash_and_payload_are_independent_of_global_UI_JSON_settings()
    {
        var input = Input(); var original = ResourceProcessingWire.Serialize(input); var hash = ResourceProcessingWire.Hash(input);
        var old = Newtonsoft.Json.JsonConvert.DefaultSettings;
        try
        {
            Newtonsoft.Json.JsonConvert.DefaultSettings = () => new() {NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()};
            Assert.AreEqual(original, ResourceProcessingWire.Serialize(input));
            Assert.AreEqual(hash, ResourceProcessingWire.Hash(input));
            Assert.AreEqual(input.JobId, ResourceProcessingWire.Deserialize<ResourceProcessingInput>(original).JobId);
        }
        finally { Newtonsoft.Json.JsonConvert.DefaultSettings = old; }
    }

    [TestMethod]
    public async Task Owner_cancel_releases_prepared_payload_and_artifacts_and_is_idempotent()
    {
        var provider = await BuildOwnerServices();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider; var db = sp.GetRequiredService<BakabaseDbContext>();
        db.ResourcesV2.Add(new ResourceDbModel {Id = 13, Path = "/source/album"}); await db.SaveChangesAsync();
        var owner = sp.GetRequiredService<ResourceProcessingOwner>(); var input = Input(13);
        var prepared = await owner.PrepareAsync("manager-a", input, default);
        var artifacts = sp.GetRequiredService<ResourceProcessingArtifacts>();
        var path = artifacts.GetDirectory(prepared, "owner"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "temporary"), "bytes");
        Assert.IsTrue(await owner.AbandonAsync("manager-a", input.JobId, prepared.Token, default));
        Assert.IsFalse(Directory.Exists(path));
        Assert.IsTrue(await owner.AbandonAsync("manager-a", input.JobId, prepared.Token, default));
        var saved = (await sp.GetRequiredService<ResourceProcessingJobStore>().GetAsync(ResourceProcessingRole.Owner,
            "manager-a", input.JobId, default))!;
        Assert.AreEqual("Cancelled", saved.State); Assert.IsFalse(saved.InputJson.Contains("ResourceJson"));
        var error = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.PrepareAsync("manager-a", input, default));
        Assert.AreEqual("JobCancelled", error.Code);
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Off_device_enhancer_rule_shadows_lower_local_rule_without_automatic_local_execution()
    {
        var provider = await BuildOwnerServices(); await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>(); db.ResourcesV2.Add(new ResourceDbModel {Id = 31, Path = "/source/album"});
        await db.SaveChangesAsync(); var profiles = sp.GetRequiredService<IResourceProfileService>();
        await profiles.Add("local", "{}", null, new() {Enhancers = [new() {EnhancerId = (int)EnhancerId.Regex}]}, null, null, null, 1);
        await profiles.Add("remote", "{}", null, new() {ExecutionTarget = new(ResourceProcessingExecutionKind.Managed, "worker", "epoch"),
            Enhancers = [new() {EnhancerId = (int)EnhancerId.Regex}]}, null, null, null, 10);
        await sp.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, default);
        var resource = (await sp.GetRequiredService<IResourceService>().Get(31))!;
        Assert.AreEqual(0, (await profiles.GetEffectiveEnhancerOptions(resource)).Count);
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Attachment_result_upload_installs_owner_paths_and_rejects_worker_paths()
    {
        var provider = await BuildOwnerServices(); await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>(); db.ResourcesV2.Add(new ResourceDbModel {Id = 19, Path = "/source/album"});
        await db.SaveChangesAsync(); var owner = sp.GetRequiredService<ResourceProcessingOwner>();
        var target = (int)Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase.BakabaseEnhancerTarget.Cover;
        var input = new ResourceProcessingInput(Guid.NewGuid().ToString("N"), "owner", "epoch", 19, null, new string('a', 64),
            ResourceProcessingKind.Enhancement, Enhancer: new() {EnhancerId = (int)EnhancerId.Bakabase,
                TargetOptions = [new() {Target = target, PropertyPool = PropertyPool.Reserved, PropertyId = (int)ReservedProperty.Cover}]});
        var prepared = await owner.PrepareAsync("manager-a", input, default);
        var bytes = new byte[] {1, 2, 3, 4};
        var artifact = new ResourceProcessingArtifact(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "cover.jpg", bytes.Length);
        Assert.AreEqual(4L, await owner.PutArtifactChunkAsync("manager-a", new(input.JobId, prepared.Token, artifact, 0,
            Convert.ToBase64String(bytes)), default));
        var reference = new List<string> {ResourceProcessingWire.ArtifactPrefix + artifact.Hash};
        var output = new ResourceProcessingOutput(ResourceProcessingKind.Enhancement, [],
            [new(target, null, StandardValueType.ListString, reference.SerializeAsStandardValue(StandardValueType.ListString))], [artifact], 1);
        await owner.ApplyAsync("manager-a", input.JobId, prepared.Token, output, default);
        var resource = (await sp.GetRequiredService<IResourceService>().Get(19,
            Bakabase.InsideWorld.Models.Constants.AdditionalItems.ResourceAdditionalItem.Properties))!;
        var stored = (IEnumerable<string>)resource.Properties![(int)PropertyPool.Reserved][(int)ReservedProperty.Cover].Values!
            .First(v => v.Scope == (int)PropertyValueScope.Bakabase).BizValue!;
        var path = stored.Single();
        Assert.IsTrue(File.Exists(path)); CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        Assert.IsFalse(path.Contains("processing/worker"));
        // A later step may only refer to verified artifact ids or resource-relative paths.
        var otherInput = input with {JobId = Guid.NewGuid().ToString("N")};
        var other = await owner.PrepareAsync("manager-a", otherInput, default);
        var malicious = output with {Artifacts = [], Values = [new(target, null, StandardValueType.ListString,
            new List<string> {"C:\\worker-private\\secret.jpg"}.SerializeAsStandardValue(StandardValueType.ListString))]};
        var rejected = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.ApplyAsync("manager-a",
            otherInput.JobId, other.Token, malicious, default));
        Assert.AreEqual("UnsafePath", rejected.Code);
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task Owner_preparation_pairs_fresh_DB_metadata_with_its_revision_even_after_cache_warmup()
    {
        var provider = await BuildOwnerServices(); await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BakabaseDbContext>(); db.ResourcesV2.Add(new ResourceDbModel {Id = 27, Path = "/source/before"});
        await db.SaveChangesAsync(); var resources = sp.GetRequiredService<IResourceService>();
        Assert.AreEqual("/source/before", (await resources.Get(27))!.Path);
        await sp.GetRequiredService<MetadataSourceJournal>().EnsureAsync(default);
        await db.Database.ExecuteSqlRawAsync("UPDATE ResourcesV2 SET Path='/source/committed' WHERE Id=27");
        var input = Input(27);
        var prepared = await sp.GetRequiredService<ResourceProcessingOwner>().PrepareAsync("manager-a", input, default);
        Assert.AreEqual("/source/committed", ResourceProcessingWire.Deserialize<Resource>(prepared.ResourceJson).Path);
        Assert.AreEqual((await sp.GetRequiredService<MetadataSourceJournal>().RevisionsAsync([27], default))[27], prepared.ResourceRevision);
        if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync();
    }

    [TestMethod]
    public async Task New_epoch_preparation_reclaims_only_same_requesters_expired_owner_payloads()
    {
        var provider = await BuildOwnerServices();
        try
        {
            await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<BakabaseDbContext>();
            db.ResourcesV2.Add(new ResourceDbModel {Id = 27, Path = "/source/current"}); await db.SaveChangesAsync();
            var jobs = sp.GetRequiredService<ResourceProcessingJobStore>();
            for (var i = 0; i < 16; i++)
                await jobs.CreateAsync(ResourceProcessingRole.Owner, "manager-a", Input(27) with {OwnerEpoch = "expired"},
                    ResourceProcessingWire.Serialize(new ResourceProcessingOwnerState(null, new string('a', 64), [])), default);
            var other = await jobs.CreateAsync(ResourceProcessingRole.Owner, "manager-b", Input(27) with {OwnerEpoch = "expired"},
                ResourceProcessingWire.Serialize(new ResourceProcessingOwnerState(null, new string('a', 64), [])), default);
            var prepared = await sp.GetRequiredService<ResourceProcessingOwner>().PrepareAsync("manager-a", Input(27), default);
            Assert.AreEqual("epoch", prepared.Input.OwnerEpoch);
            Assert.AreEqual(16, await db.ResourceProcessingJobs.CountAsync(j => j.RequesterId == "manager-a" && j.State == "Cancelled"));
            Assert.AreEqual("Queued", (await jobs.GetByIdAsync(other.Id, default))!.State);
        }
        finally { if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }

    [TestMethod]
    public async Task Abandon_before_prepare_blocks_a_late_request_without_touching_other_requesters()
    {
        var provider = await BuildOwnerServices();
        try
        {
            await using var scope = provider.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<BakabaseDbContext>();
            db.ResourcesV2.Add(new ResourceDbModel {Id = 27, Path = "/source/current"}); await db.SaveChangesAsync();
            var owner = sp.GetRequiredService<ResourceProcessingOwner>(); var input = Input(27);
            Assert.IsTrue(await owner.AbandonAsync("manager-a", input.JobId, null, default));
            Assert.IsTrue(await owner.AbandonAsync("manager-a", input.JobId, null, default));
            var cancelled = await Assert.ThrowsExceptionAsync<ResourceProcessingException>(() => owner.PrepareAsync("manager-a", input, default));
            Assert.AreEqual("JobCancelled", cancelled.Code);
            var allowed = await owner.PrepareAsync("manager-b", input, default);
            Assert.AreEqual(input.JobId, allowed.Input.JobId);
            Assert.AreEqual(0, await db.ResourceCaches.CountAsync());
        }
        finally { if (provider is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
    }

    private Task<IServiceProvider> BuildOwnerServices() => TestServiceBuilder.BuildServiceProvider(services =>
    {
        services.AddSingleton<INodeIdentityProvider>(new Identity("owner", "epoch"));
        services.AddSingleton<IFederationDataDirectory>(new DataDirectory(_directory));
        services.AddSingleton<MetadataSourceJournalGate>();
        services.AddScoped<MetadataSourceJournal>();
        services.AddScoped<ResourceProcessingJobStore>();
        services.AddScoped<ResourceProcessingOwner>();
        services.AddSingleton<ResourceProcessingArtifacts>();
    });
    private sealed class Identity(string id, string epoch) : INodeIdentityProvider
    { public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NodeIdentity(id, epoch, id)); }
    private sealed class DataDirectory(string path) : IFederationDataDirectory
    { public string Path => path; public string Ensure() {Directory.CreateDirectory(path); return path;} }
    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)proxy).Call = call; return proxy;
    }
    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }
}
