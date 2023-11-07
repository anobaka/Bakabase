using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    public class BulkModificationController : Controller
    {
        public async Task<BaseResponse> Update(int id, [FromBody] BulkModificationPutRequestModel model)
        {

        }

        public async Task Filter(int id)
        {
            BulkModificationDto bulkModification = new();
            var rootFilter = bulkModification.Filter;
            var resources = new List<ResourceDto>();
            if (rootFilter != null)
            {
                var exp = rootFilter.BuildExpression();
                resources = resources.Where(exp.Compile()).ToList();
            }
        }
    }
}