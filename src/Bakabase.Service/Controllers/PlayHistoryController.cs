using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.Modules.Alias.Abstractions.Models.Domain;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Models.Input;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/play-history")]
    public class PlayHistoryController(IPlayHistoryService service, IGuiAdapter guiAdapter) : Controller
    {
        [SwaggerOperation(OperationId = "SearchPlayHistories")]
        [HttpGet]
        public async Task<SearchResponse<PlayHistoryDbModel>> SearchGroups(PlayHistorySearchInputModel model)
        {
            return await service.Search(null, model.PageIndex, model.PageSize);
        }
    }
}