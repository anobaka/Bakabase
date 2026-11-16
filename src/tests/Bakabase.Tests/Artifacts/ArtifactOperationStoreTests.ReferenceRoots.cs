using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [TestMethod]
    public async Task File_reference_checks_do_not_expand_unrelated_manifests()
    {
        var target = await Adopt("bounded-target", await Files("bounded-target"));
        var unrelated = await Adopt("bounded-unrelated", await Files("bounded-unrelated"));
        // Invalid unrelated manifest content is a deterministic witness that this root
        // was not parsed/hashed. A candidate at that root must still fail closed.
        await _db.Artifacts.Where(a => a.Id == unrelated.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ManifestJson, "not-json"));
        Assert.IsFalse(await _store.HasOtherRegisteredFilesAsync(target.Files, target.Id));
        Assert.IsFalse(await _store.HasRegisteredFilesAsync([Path.Combine(_root, "other.txt")]));
        await Assert.ThrowsExceptionAsync<System.Text.Json.JsonException>(() => _store.HasRegisteredFilesAsync(unrelated.Files));
    }

    [TestMethod]
    public async Task Root_prefilter_keeps_ancestor_roots_and_both_absolute_and_AppData_representations()
    {
        var registration = await Files("ancestor-target");
        var target = await Adopt("ancestor-target", registration);
        var ancestor = await Adopt("ancestor-reference", registration with {RootDirectory = _appData});
        Assert.IsTrue(await _store.HasOtherRegisteredFilesAsync(target.Files, target.Id));
        // Old registrations may be absolute even when the root lies inside AppData.
        await _db.Artifacts.Where(a => a.Id == ancestor.Id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.RootIsAppData, false).SetProperty(a => a.RootPath, _appData));
        Assert.IsTrue(await _store.HasOtherRegisteredFilesAsync(target.Files, target.Id));
        Assert.IsFalse(await _store.HasOtherRegisteredFilesAsync(target.Files, target.Id, [ancestor.Id]));
        Assert.IsTrue(File.Exists(target.Files.Single()));
    }

    [TestMethod]
    public async Task Root_prefilter_preserves_platform_case_comparison_including_non_ASCII()
    {
        var target = await Adopt("case-root", await Files("case-root", root: Path.Combine(_appData, "Äpfel")));
        var alternate = target.Files.Select(p => p.Replace("Äpfel", "äPFEL", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
            await _store.HasRegisteredFilesAsync(alternate));
    }
}
