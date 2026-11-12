using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.Extensions.DependencyInjection;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [TestMethod]
    public async Task Local_coordinator_construction_and_unowned_download_never_activate_cloud_dependencies()
    {
        var activations = 0;
        var services = new ServiceCollection();
        services.AddSingleton(_db); services.AddSingleton<IArtifactOperationStore>(_store);
        services.AddSingleton<IUserStoragePolicy>(new ProvenanceTestStorage());
        services.AddSingleton(new DownloadResultService(_db, () => _appData));
        services.AddScoped<CloudDownloadDirectoryProvenanceResolver>();
        services.AddScoped<DownloadArtifactCoordinator>();
        services.AddSingleton<ICloudDownloadProvider>(_ => throw new InvalidOperationException("Network provider must stay dormant"));
        services.AddSingleton(new CloudDownloadDirectoryProvenanceRegistration("baidu", null, _ =>
        { activations++; throw new InvalidOperationException("Account must stay dormant"); }));
        await using var provider = services.BuildServiceProvider();
        Assert.IsNotNull(provider.GetRequiredService<DownloadArtifactCoordinator>());
        var source = new CloudSource {Id = "s", Url = "https://pan.baidu.com/s/share", ProviderId = "baidu"};
        var proof = await provider.GetRequiredService<CloudDownloadDirectoryProvenanceResolver>().InspectAsync(
            new("d", source, "account", new("baidu", "account", "/legacy"), _root), _root, default);
        Assert.IsNull(proof); Assert.AreEqual(0, activations);
    }

    [TestMethod]
    public async Task Directory_evidence_resolves_only_selected_provider_factory()
    {
        var selected = 0;
        var services = new ServiceCollection(); services.AddScoped<CloudDownloadDirectoryProvenanceResolver>();
        var stub = new ProvenanceTestInspector();
        services.AddSingleton(new CloudDownloadDirectoryProvenanceRegistration("baidu", null, _ => {selected++; return stub;}));
        services.AddSingleton(new CloudDownloadDirectoryProvenanceRegistration("pikpak", null, _ => throw new InvalidOperationException("Unselected account activated")));
        await using var provider = services.BuildServiceProvider();
        var request = new CloudDownloadRequest("d", new CloudSource {Id = "s", Url = "https://pan.baidu.com/s/share", ProviderId = "baidu"},
            "account", new CloudRemoteEntry("baidu", "account", "/owned") {OwnedContainer = new("t", "/owned")}, _root);
        var proof = await provider.GetRequiredService<CloudDownloadDirectoryProvenanceResolver>().InspectAsync(request, _root, default);
        Assert.IsNotNull(proof); Assert.AreEqual(1, selected); Assert.AreEqual(1, stub.Calls);
    }

    [TestMethod]
    public async Task Registration_binds_download_provenance_to_the_captured_manifest_and_reuses_without_rebuilding_metadata()
    {
        var registration = await Files("owned", root: Path.Combine(_root, "download"));
        var lengths = new Dictionary<string, long> {["nested/owned.txt"] = new FileInfo(registration.Files.Single()).Length};
        var evidence = new CloudDownloadDirectoryEvidence("nested", lengths);
        var calls = 0;
        registration = registration with {Kind = "download", CreateMetadata = manifest =>
        {
            calls++;
            return JsonSerializer.Serialize(new DownloadArtifactMetadata(42, null, evidence.Bind(manifest)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }};
        var artifact = await Adopt("existing-download", registration);
        var reused = await Adopt("existing-download", registration);
        Assert.AreEqual(artifact.Id, reused.Id);
        Assert.AreEqual(1, calls);
        var proof = FileArtifactDeliveryLayout.ReadProvenance(artifact);
        Assert.IsNotNull(proof);
        Assert.AreEqual(artifact.Fingerprint, proof.ManifestFingerprint);
        Assert.AreEqual(registration.RootDirectory, artifact.RootDirectory);
        Assert.AreEqual("nested/owned.txt", artifact.Manifest.Entries.Single().RelativePath);
        Assert.AreEqual(42, DownloadArtifactCoordinator.ReadMetadata(artifact)!.DownloadTaskId);
    }

    [TestMethod]
    public async Task Extra_or_changed_download_files_are_registered_without_unsafe_directory_evidence()
    {
        var registration = await Files("owned-mismatch", root: Path.Combine(_root, "download"));
        var evidence = new CloudDownloadDirectoryEvidence("nested", new Dictionary<string, long> {["nested/owned-mismatch.txt"] = 999});
        registration = registration with {Kind = "download", CreateMetadata = manifest => JsonSerializer.Serialize(
            new DownloadArtifactMetadata(43, null, evidence.Bind(manifest)), new JsonSerializerOptions(JsonSerializerDefaults.Web))};
        var artifact = await Adopt("unproven-download", registration);
        Assert.IsNull(FileArtifactDeliveryLayout.ReadProvenance(artifact));
        Assert.IsTrue(File.Exists(artifact.Files.Single()));
        Assert.AreEqual("nested/owned-mismatch.txt", artifact.Manifest.Entries.Single().RelativePath);
    }
    private sealed class ProvenanceTestStorage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
    private sealed class ProvenanceTestInspector : ICloudDownloadDirectoryProvenanceProvider
    {
        public int Calls { get; private set; }
        public Task<CloudDownloadDirectoryEvidence?> InspectDirectoryEvidenceAsync(CloudDownloadRequest request,
            string root, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<CloudDownloadDirectoryEvidence?>(new("owned", new Dictionary<string, long> {["owned/a"] = 1}));
        }
    }
}
