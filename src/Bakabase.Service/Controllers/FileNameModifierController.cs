using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Service.Models.Input;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Components;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Bootstrap.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Bakabase.Service.Models.View;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Abstractions;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/file-name-modifier")]
public class FileNameModifierController(IFileNameModifier modifier) : Controller
{
    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewFileNameModification")]
    public async Task<ListResponse<string>> Preview([FromBody] FileNameModifierProcessInputModel input)
    {
        foreach (var op in input.Operations.Where(op => !modifier.ValidateOperation(op)))
        {
            return ListResponseBuilder<string>.BuildBadRequest(
                $"Invalid operations: {System.Text.Json.JsonSerializer.Serialize(op)}");
        }

        var fileNames = input.FilePaths.Select(Path.GetFileName).OfType<string>().ToList();
        var newFileNames = modifier.ModifyFileNames(fileNames, input.Operations);
        var newPaths = new List<string>();
        for (var i = 0; i < input.FilePaths.Count; i++)
        {
            var oldPath = input.FilePaths[i];
            var dir = Path.GetDirectoryName(oldPath) ?? "";
            var newFileName = newFileNames[i];
            var newPath = Path.Combine(dir, newFileName).StandardizePath()!;
            newPaths.Add(newPath);
        }

        return new ListResponse<string>(newPaths);
    }

    [HttpPost("modify")]
    [SwaggerOperation(OperationId = "ModifyFileNames")]
    public async Task<ListResponse<FileRenameResult>> Modify([FromBody] FileNameModifierProcessInputModel input)
    {
        foreach (var op in input.Operations.Where(op => !modifier.ValidateOperation(op)))
        {
            return ListResponseBuilder<FileRenameResult>.BuildBadRequest(
                $"Invalid operations: {System.Text.Json.JsonSerializer.Serialize(op)}");
        }

        var results = new List<FileRenameResult>();
        var fileNames = input.FilePaths.Select(Path.GetFileName).OfType<string>().ToList();
        var newFileNames = modifier.ModifyFileNames(fileNames, input.Operations);
        for (var i = 0; i < input.FilePaths.Count; i++)
        {
            var oldPath = input.FilePaths[i];
            var dir = Path.GetDirectoryName(oldPath) ?? "";
            var newFileName = newFileNames[i];
            var newPath = Path.Combine(dir, newFileName).StandardizePath()!;
            try
            {
                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (System.IO.File.Exists(newPath))
                    {
                        throw new IOException($"目标文件已存在: {newPath}");
                    }

                    System.IO.File.Move(oldPath, newPath);
                }

                results.Add(new FileRenameResult
                {
                    OldPath = oldPath,
                    NewPath = newPath,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                results.Add(new FileRenameResult
                {
                    OldPath = oldPath,
                    NewPath = newPath,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return new ListResponse<FileRenameResult>(results);
    }
}