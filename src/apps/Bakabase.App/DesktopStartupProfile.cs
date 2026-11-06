using System.Text.Json;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;

namespace Bakabase.App;

/// <summary>Machine-local preference for the next ordinary desktop launch.</summary>
internal sealed class DesktopStartupProfile(DesktopProfileRegistry registry)
{
    private string PreferencePath => Path.Combine(registry.RootDirectory, "startup-profile.json");
    private sealed record Selection(string ProfileId, string DefaultId);

    internal string[] Apply(string[] args, string? dataDirectoryOverride)
    {
        if (DesktopProfileSelection.ParseId(args) != null || !string.IsNullOrWhiteSpace(dataDirectoryOverride))
            return args;

        var selection = Read();
        if (selection == null) return args;
        var catalog = registry.Read();
        if (selection.DefaultId != catalog.DefaultId) return args;

        var profile = catalog.Profiles.FirstOrDefault(p => p.Id == selection.ProfileId);
        if (profile == null || (!profile.RequiresSetup &&
            !Directory.Exists(AppDataLocator.ResolveEffectiveDataDirectory(profile.AnchorDirectory))))
            return args;

        return args.Append(DesktopProfileSelection.ArgumentPrefix + profile.Id).ToArray();
    }

    internal void Record(string profileId)
    {
        try
        {
            var selection = new Selection(profileId, registry.Read().DefaultId);
            if (Read() == selection) return;

            var temporary = PreferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options)) JsonSerializer.Serialize(stream, selection);
                File.Move(temporary, PreferencePath, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine("Could not remember the desktop workspace: " + error.Message);
        }
    }

    private Selection? Read()
    {
        try
        {
            using var stream = new FileStream(PreferencePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return stream.Length <= 256 ? JsonSerializer.Deserialize<Selection>(stream) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
