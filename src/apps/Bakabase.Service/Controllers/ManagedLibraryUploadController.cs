using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ManagedFileUploadInputModel(string FileName, string Base64);

/// <summary>Bounded browser uploads are stored by the owner using its normal attachment/torrent services.</summary>
[ApiController]
[Route("management/library/resources/{id:int}")]
public sealed class ManagedLibraryUploadController(IResourceService resources) : ControllerBase
{
    [HttpPost("attachment")]
    [RemoteAccessible]
    [RequestSizeLimit(ManagedLibraryOperations.MaximumUploadEnvelopeBytes)]
    [SwaggerOperation(OperationId = "UploadManagedResourceAttachment")]
    public async Task<IActionResult> Attachment(int id, [FromBody] ManagedFileUploadInputModel model)
    {
        if (await resources.Get(id) == null) return NotFound(new {code = "ResourceNotFound"});
        byte[] bytes;
        try { bytes = Decode(model, ManagedLibraryOperations.MaximumUploadFileBytes); }
        catch (Bakabase.Modules.Federation.Security.FederationAccessException ex)
        { return BadRequest(new {code = ex.ErrorCode, message = ex.Message}); }
        await using var stream = new MemoryStream(bytes, false);
        var file = new FormFile(stream, 0, bytes.Length, "file", Path.GetFileName(model.FileName));
        var files = ActivatorUtilities.CreateInstance<FileController>(HttpContext.RequestServices);
        files.ControllerContext = ControllerContext;
        return Ok(await files.UploadFile(file));
    }

    [HttpPost("torrent")]
    [RemoteAccessible]
    [RequestSizeLimit(ManagedLibraryOperations.MaximumUploadEnvelopeBytes)]
    [SwaggerOperation(OperationId = "UploadManagedResourceTorrent")]
    public async Task<IActionResult> Torrent(int id, [FromBody] ManagedFileUploadInputModel model, CancellationToken ct)
    {
        if (await resources.Get(id) == null) return NotFound(new {code = "ResourceNotFound"});
        byte[] bytes;
        try { bytes = Decode(model, 4 * 1024 * 1024); }
        catch (Bakabase.Modules.Federation.Security.FederationAccessException ex)
        { return BadRequest(new {code = ex.ErrorCode, message = ex.Message}); }
        await using var stream = new MemoryStream(bytes, false);
        var file = new FormFile(stream, 0, bytes.Length, "file", Path.GetFileName(model.FileName));
        var leads = ActivatorUtilities.CreateInstance<AcquisitionLeadController>(HttpContext.RequestServices);
        leads.ControllerContext = ControllerContext;
        return Ok(await leads.AddTorrent(id, file, ct));
    }

    [NonAction]
    public static byte[] Decode(ManagedFileUploadInputModel model, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(model.FileName) || model.FileName.Length > 255 ||
            string.IsNullOrEmpty(model.Base64) || model.Base64.Length > (maximumBytes + 2) / 3 * 4)
            throw new Bakabase.Modules.Federation.Security.FederationAccessException("InvalidResourceUpload", 400,
                "Choose a file within the upload size limit.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(model.Base64); }
        catch (FormatException)
        { throw new Bakabase.Modules.Federation.Security.FederationAccessException("InvalidResourceUpload", 400, "The uploaded file is invalid."); }
        if (bytes.Length is 0 || bytes.Length > maximumBytes)
            throw new Bakabase.Modules.Federation.Security.FederationAccessException("InvalidResourceUpload", 400,
                "Choose a file within the upload size limit.");
        return bytes;
    }
}
