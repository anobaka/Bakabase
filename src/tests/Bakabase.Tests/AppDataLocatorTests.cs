using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.Configurations.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// One answer to "which directory is the data directory", whoever asks: the single-instance
/// guard, the database, <c>app.json</c>, <see cref="AppService.AppDataDirectory"/> and the log.
/// </summary>
/// <remarks>
/// They used to disagree under <c>BAKABASE_DATA_DIR</c>: the guard (and the files and the log)
/// stopped at the variable's directory while the database and <c>app.json</c> followed a
/// <c>.redirect</c> inside it, so a launch naming an anchor that redirects to a running
/// instance's directory locked the anchor and opened that instance's database. The real-process
/// half of this is in <c>SingleInstanceGuardTests</c>.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class AppDataLocatorTests
{
    private static readonly FieldInfo DefaultDirectoryField =
        typeof(AppService).GetField("_defaultAppDataDirectory", BindingFlags.NonPublic | BindingFlags.Static) ??
        throw new InvalidOperationException(
            "AppService no longer caches its data directory in _defaultAppDataDirectory; point this test at it anew.");

    private string _root = null!;
    private string _variable = null!;
    private string? _previousVariable;
    private object? _previousDirectory;

    [TestInitialize]
    public void Setup()
    {
        // The static constructor runs once per process with the environment as it is; let it,
        // before pointing the app at this test's own directory.
        RuntimeHelpers.RunClassConstructor(typeof(AppService).TypeHandle);

        _root = Path.Combine(Path.GetTempPath(), "bakabase-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _variable = AppDataAnchor.Current.EnvVarName;
        _previousVariable = Environment.GetEnvironmentVariable(_variable);
        _previousDirectory = DefaultDirectoryField.GetValue(null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        DefaultDirectoryField.SetValue(null, _previousDirectory);
        Environment.SetEnvironmentVariable(_variable, _previousVariable);
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private AppService PointAt(string anchor)
    {
        Environment.SetEnvironmentVariable(_variable, anchor);
        DefaultDirectoryField.SetValue(null, null);
        Assert.AreEqual(anchor, AppService.DefaultAppDataDirectory, "the app is not looking at this test's directory");
        return new AppService(NullLogger<AppService>.Instance, null!, new ServiceCollection().BuildServiceProvider());
    }

    [TestMethod]
    public void Under_the_variable_a_redirect_is_followed_by_everyone()
    {
        var anchor = Path.Combine(_root, "anchor");
        var target = Path.Combine(_root, "data");
        AnchorRedirect.Write(anchor, target);

        var app = PointAt(anchor);

        Assert.AreEqual(target, AppDataLocator.ResolveEffectiveDataDirectory(), "the guard");
        Assert.AreEqual(Path.Combine(target, EffectiveAppDataResolver.AppOptionsFileName),
            AppOptionsManager.GetAppOptionsFilePath(), "app.json");
        Assert.AreEqual(target, app.AppDataDirectory, "covers, caches, keys");
        Assert.AreEqual(Path.Combine(target, "logs"), AppService.LogPath, "the log");
        Assert.AreEqual(DataPathSource.Environment, app.DataPathSource, "still reported as the variable's doing");
        Assert.AreEqual(anchor, app.AnchorPath);
    }

    [TestMethod]
    public void Under_the_variable_without_a_redirect_the_directory_is_used_as_it_is()
    {
        var volume = Path.Combine(_root, "volume");
        Directory.CreateDirectory(volume);

        var app = PointAt(volume);

        Assert.AreEqual(volume, AppDataLocator.ResolveEffectiveDataDirectory());
        Assert.AreEqual(Path.Combine(volume, EffectiveAppDataResolver.AppOptionsFileName),
            AppOptionsManager.GetAppOptionsFilePath());
        Assert.AreEqual(volume, app.AppDataDirectory);
        Assert.AreEqual(Path.Combine(volume, "logs"), AppService.LogPath);
    }
}
