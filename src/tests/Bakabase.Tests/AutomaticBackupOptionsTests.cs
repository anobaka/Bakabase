using System.Text;
using Bakabase.Infrastructures.Components.App.Models.RequestModels;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Notices;
using Bootstrap.Models.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class AutomaticBackupOptionsTests
{
    [TestMethod]
    public void New_and_legacy_options_enable_backups_and_keep_seven_versions()
    {
        const string legacy = """{"Language":"en-US","Version":"2.3.0"}""";
        var deserialized = JsonConvert.DeserializeObject<AppOptions>(legacy)!;
        var bound = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""{"AppOptions":{{legacy}}}""")))
            .Build()
            .GetSection(nameof(AppOptions))
            .Get<AppOptions>()!;

        foreach (var options in new[] {new AppOptions(), deserialized, bound})
        {
            Assert.IsTrue(options.EnableAutomaticBackup);
            Assert.AreEqual(7, options.MaxBackupVersions);
        }
    }

    [TestMethod]
    public void Saved_backup_preferences_survive_serialization_and_configuration_binding()
    {
        const string saved = """{"EnableAutomaticBackup":false,"MaxBackupVersions":12}""";
        var deserialized = JsonConvert.DeserializeObject<AppOptions>(saved)!;
        var bound = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""{"AppOptions":{{saved}}}""")))
            .Build()
            .GetSection(nameof(AppOptions))
            .Get<AppOptions>()!;
        var roundTrip = JsonConvert.DeserializeObject<AppOptions>(JsonConvert.SerializeObject(deserialized))!;

        foreach (var options in new[] {deserialized, bound, roundTrip})
        {
            Assert.IsFalse(options.EnableAutomaticBackup);
            Assert.AreEqual(12, options.MaxBackupVersions);
        }
    }

    [TestMethod]
    public async Task Patching_other_options_preserves_backup_preferences()
    {
        var original = new AppOptions {EnableAutomaticBackup = false, MaxBackupVersions = 12};
        var manager = new StubOptions<AppOptions>(original);

        var response = await Controller(manager).PatchAppOptions(new AppOptionsPatchRequestModel
        {
            EnableAnonymousDataTracking = false
        });

        Assert.AreEqual((int) ResponseCode.Success, response.Code);
        Assert.AreEqual(1, manager.SaveCount);
        Assert.IsFalse(manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(12, manager.Value.MaxBackupVersions);
        Assert.IsFalse(manager.Value.EnableAnonymousDataTracking);
        Assert.IsTrue(original.EnableAnonymousDataTracking, "The manager persists a copy of the original options.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Patching_backup_switch_preserves_retention(bool enabled)
    {
        var manager = new StubOptions<AppOptions>(new AppOptions
        {
            EnableAutomaticBackup = !enabled,
            MaxBackupVersions = 12
        });

        var response = await Controller(manager).PatchAppOptions(new AppOptionsPatchRequestModel
        {
            EnableAutomaticBackup = enabled
        });

        Assert.AreEqual((int) ResponseCode.Success, response.Code);
        Assert.AreEqual(1, manager.SaveCount);
        Assert.AreEqual(enabled, manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(12, manager.Value.MaxBackupVersions);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(12)]
    [DataRow(int.MaxValue)]
    public async Task Patching_retention_preserves_disabled_backups(int count)
    {
        var manager = new StubOptions<AppOptions>(new AppOptions {EnableAutomaticBackup = false});

        var response = await Controller(manager).PatchAppOptions(new AppOptionsPatchRequestModel
        {
            MaxBackupVersions = count
        });

        Assert.AreEqual((int) ResponseCode.Success, response.Code);
        Assert.AreEqual(1, manager.SaveCount);
        Assert.IsFalse(manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(count, manager.Value.MaxBackupVersions);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public async Task Invalid_patch_retention_rejects_every_change_without_saving(int count)
    {
        var original = new AppOptions {MaxBackupVersions = 12};
        var manager = new StubOptions<AppOptions>(original);

        var response = await Controller(manager).PatchAppOptions(new AppOptionsPatchRequestModel
        {
            EnableAutomaticBackup = false,
            MaxBackupVersions = count,
            EnableAnonymousDataTracking = false
        });

        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation, response.Code);
        Assert.AreEqual(0, manager.SaveCount);
        Assert.AreSame(original, manager.Value);
        Assert.IsTrue(manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(12, manager.Value.MaxBackupVersions);
        Assert.IsTrue(manager.Value.EnableAnonymousDataTracking);
    }

    [TestMethod]
    [DataRow(true, 1)]
    [DataRow(false, 12)]
    [DataRow(true, int.MaxValue)]
    public async Task Putting_options_persists_both_backup_preferences(bool enabled, int count)
    {
        var manager = new StubOptions<AppOptions>(new AppOptions());
        var options = new AppOptions {EnableAutomaticBackup = enabled, MaxBackupVersions = count};

        var response = await Controller(manager).PutAppOptions(options);

        Assert.AreEqual((int) ResponseCode.Success, response.Code);
        Assert.AreEqual(1, manager.SaveCount);
        Assert.AreSame(options, manager.Value);
        Assert.AreEqual(enabled, manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(count, manager.Value.MaxBackupVersions);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public async Task Invalid_put_retention_preserves_saved_options(int count)
    {
        var original = new AppOptions {MaxBackupVersions = 12};
        var manager = new StubOptions<AppOptions>(original);

        var response = await Controller(manager).PutAppOptions(new AppOptions
        {
            EnableAutomaticBackup = false,
            MaxBackupVersions = count,
            EnableAnonymousDataTracking = false
        });

        Assert.AreEqual((int) ResponseCode.InvalidPayloadOrOperation, response.Code);
        Assert.AreEqual(0, manager.SaveCount);
        Assert.AreSame(original, manager.Value);
        Assert.IsTrue(manager.Value.EnableAutomaticBackup);
        Assert.AreEqual(12, manager.Value.MaxBackupVersions);
        Assert.IsTrue(manager.Value.EnableAnonymousDataTracking);
    }

    private static OptionsController Controller(StubOptions<AppOptions> manager) =>
        new(null!, manager, null!, null!, null!, null!, null!);
}
