using System;
using System.IO;
using System.Linq;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public class DesktopProfileRegistryTests
{
    private string _root = null!;
    private DesktopProfileRegistry _registry = null!;
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-profiles-" + Guid.NewGuid().ToString("N"));
        _registry = new DesktopProfileRegistry(Path.Combine(_root, "control"), Path.Combine(_root, "legacy"));
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod] public void Reading_an_existing_single_space_does_not_move_or_write_data()
    {
        var state = _registry.Read();
        Assert.AreEqual("default", state.DefaultId);
        Assert.AreEqual(Path.Combine(_root, "legacy"), state.Profiles.Single().AnchorDirectory);
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod] public void New_spaces_have_distinct_anchors_and_do_not_create_or_initialize_the_target()
    {
        var target = Path.Combine(_root, "data");
        var added = _registry.Add("New library", target, create: true);
        Assert.AreNotEqual(target, added.AnchorDirectory);
        Assert.AreEqual(target, AppDataLocator.ResolveEffectiveDataDirectory(added.AnchorDirectory));
        Assert.IsFalse(Directory.Exists(target)); // first-start Setup still chooses/imports data
        Assert.AreEqual(2, _registry.Read().Profiles.Count);
    }

    [TestMethod] public void Register_existing_preserves_identity_and_deduplicates_redirected_paths()
    {
        var target = Path.Combine(_root, "existing");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "app.json"), "preserve me");
        var oldAnchor = Path.Combine(_root, "env-anchor");
        AnchorRedirect.Write(oldAnchor, target);
        var added = _registry.Add("Imported registration", oldAnchor, create: false);
        Assert.AreEqual(target, AppDataLocator.ResolveEffectiveDataDirectory(added.AnchorDirectory));
        Assert.AreEqual(added.Id, _registry.Add("Duplicate", target, create: false).Id);
        Assert.AreEqual("preserve me", File.ReadAllText(Path.Combine(target, "app.json")));
    }

    [TestMethod] public void Rename_default_and_remove_only_change_registry()
    {
        var target = Path.Combine(_root, "existing");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "identity.json"), "identity");
        var profile = _registry.Add("Before", target, false);
        _registry.Update(profile.Id, "After", makeDefault: true);
        Assert.AreEqual("After", _registry.Read().Profiles.Single(p => p.Id == profile.Id).Name);
        Assert.AreEqual(profile.Id, _registry.Read().DefaultId);
        _registry.Remove(profile.Id, currentProfileId: "default");
        Assert.AreEqual("default", _registry.Read().DefaultId);
        Assert.AreEqual("identity", File.ReadAllText(Path.Combine(target, "identity.json")));
        Assert.IsTrue(Directory.Exists(profile.AnchorDirectory));
    }

    [TestMethod] public void Successful_setup_remembers_that_disconnected_storage_must_not_be_reinitialized()
    {
        var added = _registry.Add("New", Path.Combine(_root, "data"), true);
        Assert.IsTrue(added.RequiresSetup);
        _registry.MarkInitialized(added.Id);
        Assert.IsFalse(_registry.Get(added.Id).RequiresSetup);
        _registry.MarkInitialized("default");
        Assert.IsFalse(_registry.Get("default").RequiresSetup);
    }

    [TestMethod] public void New_must_be_empty_and_never_reuse_existing_data()
    {
        var target = Path.Combine(_root, "existing");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "app.json"), "identity");
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Add("New", target, true));
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Add("Nested", Path.Combine(target, "inside"), true));
    }

    [TestMethod] public void Rejects_control_and_install_directories_and_current_removal()
    {
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Add("Control", Path.Combine(_root, "control", "data"), true));
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Add("Installation", System.AppContext.BaseDirectory, true));
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Remove("default", "default"));
        Assert.ThrowsException<ArgumentException>(() => _registry.Add("Relative", "relative", true));
    }

    [TestMethod] public void Setup_cannot_move_a_space_into_control_or_another_registered_space()
    {
        var target = Path.Combine(_root, "new");
        _registry.Add("New", target, true);
        Assert.ThrowsException<InvalidOperationException>(() => _registry.ValidateDataTarget(target, Path.Combine(_root, "legacy")));
        Assert.ThrowsException<InvalidOperationException>(() => _registry.ValidateDataTarget(Path.Combine(_registry.RootDirectory, "inside")));
        _registry.ValidateDataTarget(Path.Combine(_root, "independent"), Path.Combine(_root, "legacy"));
    }

    [TestMethod] public void Corrupt_registry_fails_closed_without_replacing_it()
    {
        Directory.CreateDirectory(_registry.RootDirectory);
        var file = Path.Combine(_registry.RootDirectory, "profiles.json");
        File.WriteAllText(file, "broken");
        Assert.ThrowsException<InvalidOperationException>(() => _registry.Read());
        Assert.AreEqual("broken", File.ReadAllText(file));
    }
}
