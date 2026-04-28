using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Components.Tools;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI;

/// <summary>
/// Built-in LLM tools for resource operations. Methods are exposed as AIFunctions
/// for tool calling support in LLM conversations.
/// </summary>
public class ResourceTools : ILlmTool
{
    [Description("Get information about a file including its name, size, and modification time")]
    public Task<string> GetFileInfo(
        [Description("Full path to the file")] string filePath)
    {
        if (!File.Exists(filePath))
            return Task.FromResult($"File not found: {filePath}");

        var info = new FileInfo(filePath);
        return Task.FromResult(
            $"Name: {info.Name}, Size: {info.Length} bytes, Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
    }

    [Description("List files in a directory")]
    public Task<string[]> ListFiles(
        [Description("Directory path")] string directoryPath,
        [Description("Search pattern (e.g., *.txt). Use * to match all files.")] string searchPattern)
    {
        if (!Directory.Exists(directoryPath))
            return Task.FromResult(new[] { $"Directory not found: {directoryPath}" });

        var files = Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);
        return Task.FromResult(files.Select(Path.GetFileName).Where(f => f != null).ToArray()!);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetFileInfo);
        yield return AIFunctionFactory.Create(ListFiles);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "GetFileInfo", Description = "Get file information (name, size, modification time)", IsReadOnly = true };
        yield return new LlmToolMetadata { Name = "ListFiles", Description = "List files in a directory", IsReadOnly = true };
    }
}
