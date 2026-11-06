using Bakabase.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public sealed class DesktopStartupProfileTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bakabase-startup-profile-" + Guid.NewGuid().ToString("N"));
    private DesktopProfileRegistry _registry = null!;

    [TestInitialize]
    public void Initialize() => _registry = new(Path.Combine(_root, "control"), Path.Combine(_root, "legacy"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public void Ordinary_launch_restores_the_last_workspace_and_keeps_launch_arguments()
    {
        var profile = RegisterExisting("recent");
        new DesktopStartupProfile(_registry).Record(profile.Id);

        CollectionAssert.AreEqual(new[] { "--existing-argument", "--profile=" + profile.Id },
            new DesktopStartupProfile(_registry).Apply(["--existing-argument"], null));
        Assert.AreEqual("default", _registry.Read().DefaultId);
    }

    [TestMethod]
    public void Explicit_profile_and_environment_launches_take_priority()
    {
        var profile = RegisterExisting("recent");
        var startup = new DesktopStartupProfile(_registry);
        startup.Record(profile.Id);

        CollectionAssert.AreEqual(new[] { "--profile=default" }, startup.Apply(["--profile=default"], null));
        Assert.AreEqual(0, startup.Apply([], Path.Combine(_root, "environment")).Length);
    }

    [TestMethod]
    public void A_missing_or_disconnected_remembered_workspace_falls_back_to_the_default()
    {
        var startup = new DesktopStartupProfile(_registry);
        Assert.AreEqual(0, startup.Apply([], null).Length);

        var profile = RegisterExisting("recent");
        startup.Record(profile.Id);
        Directory.Delete(Path.Combine(_root, "recent"));
        Assert.AreEqual(0, startup.Apply([], null).Length);

        Directory.CreateDirectory(Path.Combine(_root, "recent"));
        _registry.Remove(profile.Id, currentProfileId: "default");
        Assert.AreEqual(0, startup.Apply([], null).Length);
    }

    [TestMethod]
    public void Changing_the_configured_default_overrides_the_previous_launch_preference()
    {
        var recent = RegisterExisting("recent");
        var fallback = RegisterExisting("fallback");
        var startup = new DesktopStartupProfile(_registry);
        startup.Record(recent.Id);

        _registry.Update(fallback.Id, fallback.Name, makeDefault: true);

        Assert.AreEqual(0, startup.Apply([], null).Length);
        Assert.AreEqual(fallback.Id, _registry.Read().DefaultId);
    }

    private DesktopProfile RegisterExisting(string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        return _registry.Add(name, path, create: false);
    }
}
