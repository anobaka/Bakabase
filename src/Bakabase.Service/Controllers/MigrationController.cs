using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Migration;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/migration")]
    public class MigrationController(MigrationHelper migrationHelper) : Controller
    {
        [HttpPost("categories-media-libraries-and-resources-to-new-media-library")]
        [SwaggerOperation(OperationId = "MigrateCategoriesMediaLibrariesAndResourcesToNewMediaLibrary")]
        public async Task<BaseResponse> MigrateCategoriesMediaLibrariesAndResourcesToNewMediaLibrary()
        {
            await migrationHelper.MigrateCategoriesMediaLibrariesAndResources();
            return BaseResponseBuilder.Ok;
        }
    }
}


