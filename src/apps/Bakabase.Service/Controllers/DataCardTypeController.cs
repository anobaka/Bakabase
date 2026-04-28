using System.Threading.Tasks;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/data-card-type")]
public class DataCardTypeController(IDataCardTypeService service) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllDataCardTypes")]
    public async Task<ListResponse<DataCardType>> GetAll()
    {
        var types = await service.GetAll();
        return new ListResponse<DataCardType>(types);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetDataCardType")]
    public async Task<SingletonResponse<DataCardType>> Get(int id)
    {
        var type = await service.GetById(id);
        return new SingletonResponse<DataCardType>(type);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddDataCardType")]
    public async Task<SingletonResponse<DataCardType>> Add([FromBody] DataCardTypeAddInputModel model)
    {
        return await service.Add(model);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdateDataCardType")]
    public async Task<BaseResponse> Update(int id, [FromBody] DataCardTypeUpdateInputModel model)
    {
        return await service.Update(id, model);
    }

    [HttpPut("{id:int}/display-template")]
    [SwaggerOperation(OperationId = "UpdateDataCardTypeDisplayTemplate")]
    public async Task<BaseResponse> UpdateDisplayTemplate(int id,
        [FromBody] DataCardDisplayTemplate template)
    {
        return await service.UpdateDisplayTemplate(id, template);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteDataCardType")]
    public async Task<BaseResponse> Delete(int id)
    {
        return await service.Delete(id);
    }
}
