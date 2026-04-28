using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/data-card")]
public class DataCardController(IDataCardService service) : Controller
{
    [HttpGet("search")]
    [SwaggerOperation(OperationId = "SearchDataCards")]
    public async Task<SearchResponse<DataCard>> Search([FromQuery] DataCardSearchInputModel model)
    {
        return await service.Search(model);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetDataCard")]
    public async Task<SingletonResponse<DataCard>> Get(int id)
    {
        var card = await service.GetById(id);
        return new SingletonResponse<DataCard>(card);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddDataCard")]
    public async Task<SingletonResponse<DataCard>> Add([FromBody] DataCardAddInputModel model)
    {
        return await service.Add(model);
    }

    [HttpPost("find-by-identity")]
    [SwaggerOperation(OperationId = "FindDataCardByIdentity")]
    public async Task<SingletonResponse<DataCard>> FindByIdentity(
        [FromBody] DataCardFindByIdentityInputModel model)
    {
        return await service.FindByIdentity(model);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdateDataCard")]
    public async Task<BaseResponse> Update(int id, [FromBody] DataCardUpdateInputModel model)
    {
        return await service.Update(id, model);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteDataCard")]
    public async Task<BaseResponse> Delete(int id)
    {
        return await service.Delete(id);
    }

    [HttpDelete("type/{typeId:int}")]
    [SwaggerOperation(OperationId = "DeleteDataCardsByType")]
    public async Task<BaseResponse> DeleteByType(int typeId)
    {
        return await service.DeleteByTypeId(typeId);
    }

    [HttpPost("type/{typeId:int}/initial-data")]
    [SwaggerOperation(OperationId = "CreateInitialDataCards")]
    public async Task<SingletonResponse<int>> CreateInitialData(int typeId,
        [FromBody] DataCardCreateInitialDataInputModel model)
    {
        return await service.CreateInitialData(typeId, model.OnlyFromResources, model.AllowNullPropertyIds);
    }

    [HttpPost("type/{typeId:int}/initial-data/preview")]
    [SwaggerOperation(OperationId = "PreviewInitialDataCards")]
    public async Task<SingletonResponse<DataCardInitialDataPreview>> PreviewInitialData(int typeId,
        [FromBody] DataCardCreateInitialDataInputModel model)
    {
        return await service.PreviewInitialData(typeId, model.OnlyFromResources, model.AllowNullPropertyIds);
    }

    [HttpGet("resource/{resourceId:int}/associated")]
    [SwaggerOperation(OperationId = "GetAssociatedDataCards")]
    public async Task<ListResponse<DataCard>> GetAssociatedCards(int resourceId)
    {
        var cards = await service.GetAssociatedCards(resourceId);
        return new ListResponse<DataCard>(cards);
    }

    [HttpGet("{id:int}/associated-resource-ids")]
    [SwaggerOperation(OperationId = "GetAssociatedResourceIds")]
    public async Task<ListResponse<int>> GetAssociatedResourceIds(int id)
    {
        var resourceIds = await service.GetAssociatedResourceIds(id);
        return new ListResponse<int>(resourceIds);
    }
}
