using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public record FileSystemEntryNameViewModel(string Path, string Name, bool IsDirectory);