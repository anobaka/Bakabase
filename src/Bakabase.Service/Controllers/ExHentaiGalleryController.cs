using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/exhentai-gallery")]
public class ExHentaiGalleryController(IExHentaiGalleryService service) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllExHentaiGalleries")]
    public async Task<ListResponse<ExHentaiGalleryDbModel>> GetAll()
    {
        var data = await service.GetAll();
        return new ListResponse<ExHentaiGalleryDbModel>(data);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteExHentaiGallery")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.DeleteById(id);
        return BaseResponseBuilder.Ok;
    }
}
