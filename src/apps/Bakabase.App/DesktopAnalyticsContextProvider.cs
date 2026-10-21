using System.Text;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.App;

/// <summary>A single anonymous desktop identity across profile changes. It is never a pairing identity.</summary>
public sealed class DesktopAnalyticsContextProvider : IDesktopAnalyticsContextProvider
{
    private readonly string _directory;
    private readonly Func<bool> _enabled;
    private readonly string _version;
    private readonly object _gate = new();
    private string? _id;

    public DesktopAnalyticsContextProvider(IBOptions<AppOptions> options)
        : this(DesktopProfileRegistry.ForCurrentUser().RootDirectory,
            () => options.Value.EnableAnonymousDataTracking, AppService.CoreVersion.ToString()) { }

    public DesktopAnalyticsContextProvider(string directory, Func<bool> enabled, string version)
    {
        _directory = directory;
        _enabled = enabled;
        _version = version;
    }

    public DesktopAnalyticsContext Read()
    {
        if (!_enabled()) return new(null, _version, false);
        lock (_gate)
        {
            try
            {
                if (_id == null)
                {
                    var path = Path.Combine(_directory, "analytics-client-id");
                    if (File.Exists(path))
                    {
                        if (new FileInfo(path).Length > 64 || !Guid.TryParse(File.ReadAllText(path).Trim(), out var existing) || existing == Guid.Empty)
                            return new(null, _version, false);
                        _id = existing.ToString("D");
                    }
                    else
                    {
                        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_directory);
                        else Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                        var id = Guid.NewGuid().ToString("D");
                        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                        using (var file = new FileStream(path, options)) file.Write(Encoding.ASCII.GetBytes(id));
                        _id = id;
                    }
                }
                return new(_id, _version, true);
            }
            catch (IOException) { return new(null, _version, false); }
            catch (UnauthorizedAccessException) { return new(null, _version, false); }
        }
    }
}
