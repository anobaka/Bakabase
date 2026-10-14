using System.Text.Json;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public sealed class DevicePlaybackOverridesStoreTests
{
    private string _root = null!;
    private string PreferencesPath => Path.Combine(_root, "configs", DevicePlaybackPreferencesStore.FileName);
    private string FilePath => Path.Combine(_root, "configs", DevicePlaybackOverridesStore.FileName);

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "bakabase-opening-rule-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private DevicePlaybackOverridesStore Store(Func<string?>? resolve = null) => new(Options.Create(
        new PlayerModuleOptions { DevicePlaybackPreferencesFile = resolve ?? (() => PreferencesPath) }));
    private static DevicePlaybackOverride Rule(string target = "server:a", int? resource = null,
        string kind = "video", string mode = "native", string? player = "Vlc") => new()
    {
        Target = target, ResourceId = resource, Choices = new() { [kind] = new(mode, player) }
    };

    [TestMethod]
    public async Task Missing_or_unconfigured_store_reads_empty_and_never_writes_a_fallback_location()
    {
        (await Store().ReadAsync("server:a")).Should().BeEmpty();
        var unconfigured = new DevicePlaybackOverridesStore(Options.Create(new PlayerModuleOptions()));
        (await unconfigured.ReadAsync("server:a")).Should().BeEmpty();
        Func<Task> write = () => unconfigured.SaveAsync(Rule());
        await write.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [TestMethod]
    public async Task Resource_rules_override_only_the_selected_kind_and_preserve_other_scopes()
    {
        var store = Store();
        await store.SaveAsync(Rule() with { Choices = new() { ["video"] = new("native", "Vlc"), ["image"] = new("browser") } });
        await store.SaveAsync(Rule(resource: 42, player: "Mpv"));
        await store.SaveAsync(Rule("server:b", 42, mode: "ask", player: null));
        (await store.ResolveAsync("server:a", 42, "video"))!.PlayerId.Should().Be("Mpv");
        (await store.ResolveAsync("server:a", 42, "image"))!.Mode.Should().Be("browser");
        (await store.ResolveAsync("server:a", 43, "video"))!.PlayerId.Should().Be("Vlc");
        (await store.ResolveAsync("server:b", 42, "video"))!.Mode.Should().Be("ask");
        (await store.ResolveAsync("server:b", 43, "video")).Should().BeNull();
        (await store.ReadAsync("server:a")).Should().HaveCount(2);
        (await store.ReadAsync("server:b")).Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Equal_resource_ids_on_a_new_library_epoch_do_not_inherit_old_rules()
    {
        var store = Store();
        await store.SaveAsync(Rule("node:peer:epoch-1", 42));
        (await store.ResolveAsync("node:peer:epoch-1", 42, "video"))!.PlayerId.Should().Be("Vlc");
        (await store.ResolveAsync("node:peer:epoch-2", 42, "video")).Should().BeNull();
        (await store.ResolveAsync("node:other:epoch-1", 42, "video")).Should().BeNull();
        (await store.ResolveAsync("server:peer", 42, "video")).Should().BeNull();
    }

    [TestMethod]
    public async Task Empty_choices_reset_one_scope_to_inheritance_without_changing_other_rules()
    {
        var store = Store();
        await store.SaveAsync(Rule());
        await store.SaveAsync(Rule(resource: 42, player: "Mpv"));
        await store.SaveAsync(Rule("server:b"));
        await store.SaveAsync(new() { Target = "server:a", ResourceId = 42, Choices = [] });
        (await store.ResolveAsync("server:a", 42, "video"))!.PlayerId.Should().Be("Vlc");
        (await store.ReadAsync("server:a")).Should().ContainSingle();
        (await store.ReadAsync("server:b")).Should().ContainSingle();
        await store.SaveAsync(new() { Target = "server:a", Choices = [] });
        (await store.ReadAsync("server:a")).Should().BeEmpty();
        (await store.ReadAsync("server:b")).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Separate_instances_merge_concurrent_scopes_and_never_publish_partial_json()
    {
        var stores = Enumerable.Range(0, 4).Select(_ => Store()).ToArray();
        var writes = Enumerable.Range(1, 40).Select(id => stores[id % 4].SaveAsync(Rule(resource: id))).ToArray();
        for (var i = 0; i < 10; ++i)
            (await stores[0].ReadAsync("server:a")).Should().OnlyContain(r => r.Target == "server:a");
        await Task.WhenAll(writes);
        (await Store().ReadAsync("server:a")).Should().HaveCount(40);
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(FilePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [DataTestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[null]")]
    [DataRow("[{\"target\":\"server:a\"}]")]
    [DataRow("[{\"choices\":{}}]")]
    [DataRow("[{\"target\":\"server:a\",\"choices\":{},\"executablePath\":\"/tmp/run\"}]")]
    [DataRow("[{\"target\":\"server:a\",\"choices\":{\"video\":{\"mode\":\"native\",\"executablePath\":\"/tmp/run\"}}}]")]
    [DataRow("[{\"target\":\"server:a\",\"choices\":{}},{\"target\":\"server:a\",\"choices\":{}}]")]
    public async Task A_malformed_file_is_preserved_and_blocks_a_write_that_would_destroy_it(string original)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, original);
        Func<Task> read = () => Store().ReadAsync("server:a");
        await read.Should().ThrowAsync<JsonException>();
        Func<Task> write = () => Store().SaveAsync(Rule());
        await write.Should().ThrowAsync<JsonException>();
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);
    }

    [DataTestMethod]
    [DataRow("file:/tmp/a")]
    [DataRow("server:../a")]
    [DataRow("node:peer")]
    [DataRow("node:peer:")]
    [DataRow("server:a:epoch")]
    public async Task Invalid_target_scopes_never_reach_the_store(string target)
    {
        Func<Task> read = () => Store().ReadAsync(target);
        await read.Should().ThrowAsync<ArgumentException>();
        Func<Task> write = () => Store().SaveAsync(Rule(target));
        await write.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow("/usr/bin/vlc")]
    [DataRow("C:\\player.exe")]
    [DataRow("vlc --command")]
    [DataRow("https://example.org/run")]
    [DataRow("unknown-player")]
    public async Task Only_catalog_ids_or_system_default_are_valid_player_choices(string player)
    {
        Func<Task> write = () => Store().SaveAsync(Rule(player: player));
        await write.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [TestMethod]
    public async Task Invalid_kinds_modes_null_choices_and_resource_ids_are_rejected_without_touching_saved_data()
    {
        var store = Store();
        await store.SaveAsync(Rule());
        var original = await File.ReadAllTextAsync(FilePath);
        foreach (var invalid in new[] { Rule(kind: "executable"), Rule(mode: "automatic"), Rule(mode: "browser"), Rule(resource: 0), Rule(resource: -1), Rule() with { Choices = null! }, Rule() with { Choices = new() { ["video"] = null! } } })
        {
            Func<Task> write = () => store.SaveAsync(invalid);
            await write.Should().ThrowAsync<ArgumentException>();
        }
        Func<Task> nullWrite = () => store.SaveAsync(null!);
        await nullWrite.Should().ThrowAsync<ArgumentNullException>();
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);
    }

    [TestMethod]
    public async Task Path_changes_are_resolved_lazily_and_a_write_uses_one_captured_directory()
    {
        var destination = PreferencesPath;
        var store = Store(() => destination);
        await store.SaveAsync(Rule());
        var original = await File.ReadAllTextAsync(FilePath);
        destination = Path.Combine(_root, "relocated", "device-playback.json");
        (await store.ReadAsync("server:a")).Should().BeEmpty();
        await store.SaveAsync(Rule(player: "Mpv"));
        (await store.ResolveAsync("server:a", 42, "video"))!.PlayerId.Should().Be("Mpv");
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);

        var calls = 0;
        var captured = Store(() => ++calls == 1 ? PreferencesPath : destination);
        await captured.SaveAsync(Rule(resource: 42));
        calls.Should().Be(1, "the read-modify-write operation must not mix two AppData directories");
        (await Store().ReadAsync("server:a")).Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Cancelled_write_keeps_the_previous_rules_without_temporary_files()
    {
        var store = Store();
        await store.SaveAsync(Rule());
        var original = await File.ReadAllTextAsync(FilePath);
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        Func<Task> write = () => store.SaveAsync(Rule(player: "Mpv"), ct.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }
}
