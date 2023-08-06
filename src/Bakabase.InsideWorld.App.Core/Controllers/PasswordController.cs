using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/password")]
    public class PasswordController : Controller
    {
        private readonly PasswordService _service;

        public PasswordController(PasswordService service)
        {
            _service = service;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "SearchPasswords")]
        public async Task<SearchResponse<Password>> Search(PasswordSearchRequestModel model)
        {
            return await _service.Search(model);
        }

        [HttpGet("all")]
        [SwaggerOperation(OperationId = "GetAllPasswords")]
        public async Task<ListResponse<Password>> GetAll()
        {
            return new ListResponse<Password>(await _service.GetAll());
        }

        [HttpDelete("{password}")]
        [SwaggerOperation(OperationId = "DeletePassword")]
        public async Task<BaseResponse> Delete(string password)
        {
            return await _service.RemoveByKey(password);
        }
    }
}