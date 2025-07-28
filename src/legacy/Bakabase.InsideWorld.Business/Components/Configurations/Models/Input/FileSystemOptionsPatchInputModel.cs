using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Configs;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class FileSystemOptionsPatchInputModel
{
    public FileSystemOptions.FileMoverOptions? FileMover { get; set; }
    public List<string>? RecentMovingDestinations { get; set; }
    public FileSystemOptions.FileProcessorOptions? FileProcessor { get; set; }
}