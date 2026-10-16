using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Tests;

[TestClass]
public class DevicePlaybackPreferencesStoreTests
{
    private string _root = null!;
    private string FilePath => Path.Combine(_root, "configs", DevicePlaybackPreferencesStore.FileName);

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "bakabase-device-playback-tests",
        Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private DevicePlaybackPreferencesStore Store(Func<string?>? resolve = null) =>
        new(Options.Create(new PlayerModuleOptions { DevicePlaybackPreferencesFile = resolve ?? (() => FilePath) }));

    private static DevicePlaybackPreferences Preferences(string id = "potplayer") =>
        DevicePlaybackPreferences.CreateDefault() with
        {
            Image = new DevicePlaybackChoice { Mode = "browser" },
            Text = new DevicePlaybackChoice { Mode = "ask" },
            Audio = new DevicePlaybackChoice { Mode = "native", NativeSchemeId = "vlc" },
            Video = new DevicePlaybackChoice { Mode = "native", NativeSchemeId = id }
        };

    [TestMethod]
    public async Task Missing_file_returns_automatic_defaults_without_creating_any_directory()
    {
        (await Store().ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        Directory.Exists(_root).Should().BeFalse();
    }

    [TestMethod]
    public async Task An_unconfigured_host_reads_defaults_and_never_writes_to_a_fallback_directory()
    {
        var store = new DevicePlaybackPreferencesStore(Options.Create(new PlayerModuleOptions()));
        (await store.ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        Func<Task> write = () => store.WriteAsync(Preferences());
        await write.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [TestMethod]
    public async Task Preferences_roundtrip_across_instances_without_a_stale_read_cache()
    {
        var first = Store();
        var second = Store();
        (await first.ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        await second.WriteAsync(Preferences());
        (await first.ReadAsync()).Should().Be(Preferences());
        var changed = Preferences("mpv");
        await first.WriteAsync(changed);
        (await second.ReadAsync()).Should().Be(changed);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!;
        json["version"]!.GetValue<int>().Should().Be(1);
        json["video"]!["nativeSchemeId"]!.GetValue<string>().Should().Be("mpv");
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(FilePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [DataTestMethod]
    [DataRow("invalid-json")]
    [DataRow("unknown-version")]
    [DataRow("missing-choice")]
    [DataRow("null-choice")]
    [DataRow("unknown-field")]
    [DataRow("bad-mode")]
    [DataRow("path-as-player")]
    [DataRow("too-large")]
    public async Task An_invalid_file_is_preserved_while_reading_defaults(string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var document = JsonSerializer.SerializeToNode(Preferences(), JsonSerializerOptions.Web)!;
        switch (kind)
        {
            case "unknown-version": document["version"] = 2; break;
            case "missing-choice": document.AsObject().Remove("video"); break;
            case "null-choice": document["video"] = null; break;
            case "unknown-field": document["executable"] = "/usr/bin/custom"; break;
            case "bad-mode": document["audio"]!["mode"] = "unrecognized"; break;
            case "path-as-player": document["video"]!["nativeSchemeId"] = "C:/player.exe"; break;
        }

        var original = kind == "invalid-json" ? "{not json" :
            kind == "too-large" ? new string(' ', 20 * 1024) : document.ToJsonString();
        await File.WriteAllTextAsync(FilePath, original);
        (await Store().ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task An_unsupported_version_is_rejected_without_replacing_the_last_good_file(int version)
    {
        var store = Store();
        await store.WriteAsync(Preferences());
        var original = await File.ReadAllTextAsync(FilePath);
        Func<Task> write = () => store.WriteAsync(Preferences() with { Version = version });
        await write.Should().ThrowAsync<ArgumentException>();
        (await File.ReadAllTextAsync(FilePath)).Should().Be(original);
    }

    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("Automatic")]
    [DataRow("")]
    public async Task Invalid_modes_are_rejected(string mode)
    {
        Func<Task> write = () => Store().WriteAsync(Preferences() with
        {
            Video = new DevicePlaybackChoice { Mode = mode }
        });
        await write.Should().ThrowAsync<ArgumentException>();
        File.Exists(FilePath).Should().BeFalse();
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("potplayer://")]
    [DataRow("C:\\player.exe")]
    [DataRow("/usr/bin/vlc")]
    [DataRow("../player")]
    [DataRow("player --unsafe")]
    [DataRow("プレイヤー")]
    public async Task A_player_id_cannot_be_a_URL_command_or_path(string id)
    {
        Func<Task> write = () => Store().WriteAsync(Preferences(id));
        await write.Should().ThrowAsync<ArgumentException>();
        File.Exists(FilePath).Should().BeFalse();
    }

    [TestMethod]
    public async Task Player_ids_are_bounded_and_a_null_or_missing_choice_is_rejected()
    {
        Func<Task> oversized = () => Store().WriteAsync(Preferences(new string('p', 65)));
        await oversized.Should().ThrowAsync<ArgumentException>();
        Func<Task> missing = () => Store().WriteAsync(Preferences() with { Image = null! });
        await missing.Should().ThrowAsync<ArgumentException>();
        Func<Task> nullPreferences = () => Store().WriteAsync(null!);
        await nullPreferences.Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task Different_containers_share_atomic_writes_and_reads()
    {
        var stores = Enumerable.Range(0, 4).Select(_ => Store()).ToArray();
        await stores[0].WriteAsync(Preferences("initial"));
        var writers = Enumerable.Range(0, 40)
            .Select(n => stores[n % stores.Length].WriteAsync(Preferences($"player-{n}"))).ToArray();
        for (var n = 0; n < 20; n++)
        {
            var read = await stores[n % stores.Length].ReadAsync();
            read.Validate();
            read.Video.NativeSchemeId.Should().NotBeNull("an atomic read never falls back from a partial file");
        }
        await Task.WhenAll(writers);
        var final = await stores[0].ReadAsync();
        foreach (var store in stores)
            (await store.ReadAsync()).Should().Be(final);
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [TestMethod]
    public async Task The_path_is_resolved_again_after_AppData_changes()
    {
        var destination = FilePath;
        var store = Store(() => destination);
        await store.WriteAsync(Preferences("old-root"));
        var oldContents = await File.ReadAllTextAsync(destination);
        destination = Path.Combine(_root, "relocated", "configs", DevicePlaybackPreferencesStore.FileName);
        (await store.ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        await store.WriteAsync(Preferences("new-root"));
        (await store.ReadAsync()).Video.NativeSchemeId.Should().Be("new-root");
        (await File.ReadAllTextAsync(FilePath)).Should().Be(oldContents);
    }

    [TestMethod]
    public async Task A_cancelled_write_keeps_the_previous_preferences_and_leaves_no_temporary_file()
    {
        var store = Store();
        await store.WriteAsync(Preferences());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> write = () => store.WriteAsync(Preferences("new"), cancellation.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        (await store.ReadAsync()).Should().Be(Preferences());
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [TestMethod]
    public async Task A_cancelled_read_does_not_block_later_reads_or_writes()
    {
        var store = Store();
        await store.WriteAsync(Preferences());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> read = () => store.ReadAsync(cancellation.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        (await Store().ReadAsync().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(Preferences());
        await Store().WriteAsync(Preferences("updated")).WaitAsync(TimeSpan.FromSeconds(5));
        (await store.ReadAsync()).Should().Be(Preferences("updated"));
    }

    [TestMethod]
    public async Task An_invalid_read_does_not_block_an_explicit_repair_write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, "{invalid");
        (await Store().ReadAsync()).Should().Be(DevicePlaybackPreferences.CreateDefault());
        (await File.ReadAllTextAsync(FilePath)).Should().Be("{invalid");
        await Store().WriteAsync(Preferences()).WaitAsync(TimeSpan.FromSeconds(5));
        (await Store().ReadAsync()).Should().Be(Preferences());
    }

    [TestMethod]
    public async Task A_failed_atomic_rename_removes_its_temporary_file_and_preserves_the_obstruction()
    {
        Directory.CreateDirectory(FilePath);
        var sentinel = Path.Combine(FilePath, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");
        Func<Task> write = () => Store().WriteAsync(Preferences());
        var failure = await write.Should().ThrowAsync<Exception>();
        (failure.Which is IOException or UnauthorizedAccessException).Should().BeTrue();
        (await File.ReadAllTextAsync(sentinel)).Should().Be("untouched");
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }
}
