using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.PostSources;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record DecodePostSourceImageInput([Range(0, int.MaxValue)] int Revision,
    [Required, StringLength(64, MinimumLength = 64)] string ImageId,
    [MaxLength(PostSourceQr.MaxBase64Length)] string? ImageData = null);

[ApiController]
[Route("~/post-parser/task/{id:int}/source-images")]
public sealed class PostSourceImagesController(BakabaseDbContext db, IHttpClientFactory http) : ControllerBase
{
    [HttpGet, RemoteAccessible]
    [SwaggerOperation(OperationId = "GetPostSourceImages")]
    public async Task<ActionResult<SingletonResponse<SavedPostImageList>>> Get(int id, CancellationToken ct)
    {
        var list = await Read(id, ct, includeLinks: true);
        return list == null ? NotFound() : new SingletonResponse<SavedPostImageList>(list);
    }

    [HttpPost("decode"), RemoteAccessible, RequestSizeLimit(6 * 1024 * 1024)]
    [SwaggerOperation(OperationId = "DecodePostSourceImage")]
    public async Task<ActionResult<SingletonResponse<PostImageDecodeResult>>> Decode(int id,
        [FromBody] DecodePostSourceImageInput input, CancellationToken ct)
    {
        var list = await Read(id, ct);
        if (list == null) return NotFound();
        var image = list.Revision == input.Revision ? list.Images.FirstOrDefault(i => i.Id == input.ImageId) : null;
        if (image == null) return new SingletonResponse<PostImageDecodeResult>(PostImageDecodeResult.Empty("sourceChanged"));
        using var client = http.CreateClient(PostSourceQr.Client);
        return new SingletonResponse<PostImageDecodeResult>(await PostSourceQr.ReadAsync(client, image, input.ImageData, ct));
    }

    private async Task<SavedPostImageList?> Read(int id, CancellationToken ct, bool includeLinks = false)
    {
        var row = await db.PostParserTasks.AsNoTracking().Where(t => t.Id == id && !t.IsDeleted)
            .Select(t => new {t.Revision, t.ContentSnapshotJson}).SingleOrDefaultAsync(ct);
        if (row == null) return null;
        var content = row.ContentSnapshotJson == null ? null :
            JsonSerializer.Deserialize<PostContent>(row.ContentSnapshotJson, WorkflowJson.Options);
        var links = includeLinks ? new SavedPostLinks() : null;
        var images = SavedPostImages.Read(content, links);
        return new(row.Revision, images, links?.Items ?? []);
    }
}
