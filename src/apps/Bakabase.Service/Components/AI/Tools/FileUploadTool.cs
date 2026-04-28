using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.AI.Components.Tools;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class FileUploadTool(IFileManager fileManager) : ILlmTool
{
    [Description(
        "Save a base64-encoded file to the temporary attachments directory. " +
        "Use this when the user provides a file (e.g., an image for a resource cover). " +
        "Returns the saved file path that can be used to update resource covers via SetResourcePropertyValue.")]
    public async Task<string> SaveBase64File(
        [Description("Base64-encoded file content")] string base64Content,
        [Description("Original filename with extension (e.g., 'cover.jpg')")] string fileName)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Content);
            var safeFilename = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
            var savedPath = await fileManager.Save(fileManager.GetAttachmentPath(safeFilename), bytes, default);
            return JsonSerializer.Serialize(new { Success = true, Path = savedPath });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = ex.Message });
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SaveBase64File);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "SaveBase64File", Description = "Save a base64 file to temporary storage", IsReadOnly = false };
    }
}
