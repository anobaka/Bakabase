using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;

namespace Bakabase.Service.Models.Input;

public class FileNameModifierProcessInputModel
{
    public List<string> FilePaths { get; set; } = [];
    public List<FileNameModifierOperation> Operations { get; set; } = [];
} 