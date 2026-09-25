using System;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The default (§4.7): <c>{AppData}/data-sync</c> and <c>{AppData}/backups</c> (F64, the folder
/// <c>AppService.DataBackupDirectory</c> names). The paths are resolved on first use and no folder is created here:
/// registration and resolution never touch the file system, the writer creates what it writes into.
/// </summary>
public sealed class AppDataSyncDataDirectory(AppService appService) : IDataSyncDataDirectory
{
    private readonly Lazy<string> _root = new(() => appService.AppDataDirectory);

    public string Path => System.IO.Path.Combine(_root.Value, "data-sync");
    public string BackupsPath => System.IO.Path.Combine(_root.Value, "backups");
}

/// <summary>
/// Fixed folders: the TestKit's <c>{testDir}/data-sync</c> and <c>{testDir}/backups</c>. AppService's data
/// directory is static per process (F20), so every test provider needs its own.
/// </summary>
public sealed class FixedDataSyncDataDirectory : IDataSyncDataDirectory
{
    public FixedDataSyncDataDirectory(string path, string backupsPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(backupsPath);
        Path = System.IO.Path.GetFullPath(path);
        BackupsPath = System.IO.Path.GetFullPath(backupsPath);
    }

    public string Path { get; }
    public string BackupsPath { get; }

    /// <summary>The two folders under one test directory.</summary>
    public static FixedDataSyncDataDirectory Under(string root) =>
        new(System.IO.Path.Combine(root, "data-sync"), System.IO.Path.Combine(root, "backups"));
}
