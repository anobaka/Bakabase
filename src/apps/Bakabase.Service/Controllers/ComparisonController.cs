using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Comparison.Abstractions.Services;
using Bakabase.Modules.Comparison.Extensions;
using Bakabase.Modules.Comparison.Models.Domain;
using Bakabase.Modules.Comparison.Models.Input;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using ServiceInputModel = Bakabase.Service.Models.Input;
using ModuleView = Bakabase.Modules.Comparison.Models.View;

namespace Bakabase.Service.Controllers
{
    [Route("comparison")]
    public class ComparisonController(
        IComparisonService service,
        BTaskManager taskManager,
        IPropertyService propertyService,
        IPropertyLocalizer propertyLocalizer,
        IResourceService resourceService
    ) : Controller
    {
        #region Plan CRUD

        [HttpGet("plan")]
        [SwaggerOperation(OperationId = "GetAllComparisonPlans")]
        public async Task<ListResponse<ComparisonPlanViewModel>> GetAllPlans()
        {
            var dbModels = await service.GetAllPlanDbModelsAsync();
            var viewModels = new List<ComparisonPlanViewModel>();
            foreach (var (plan, rules, groupCount) in dbModels)
            {
                var ruleViewModels = rules.Select(r => r.ToViewModel()).ToList();
                var viewModel = await plan.ToServiceViewModel(ruleViewModels, propertyService, propertyLocalizer, resourceService, groupCount);
                viewModels.Add(viewModel);
            }
            return new ListResponse<ComparisonPlanViewModel>(viewModels);
        }

        [HttpGet("plan/{id:int}")]
        [SwaggerOperation(OperationId = "GetComparisonPlan")]
        public async Task<SingletonResponse<ComparisonPlanViewModel?>> GetPlan(int id)
        {
            var result = await service.GetPlanDbModelAsync(id);
            if (result == null)
            {
                return new SingletonResponse<ComparisonPlanViewModel?>(null);
            }

            var (plan, rules, groupCount) = result.Value;
            var ruleViewModels = rules.Select(r => r.ToViewModel()).ToList();
            var viewModel = await plan.ToServiceViewModel(ruleViewModels, propertyService, propertyLocalizer, resourceService, groupCount);
            return new SingletonResponse<ComparisonPlanViewModel?>(viewModel);
        }

        [HttpPost("plan")]
        [SwaggerOperation(OperationId = "CreateComparisonPlan")]
        public async Task<SingletonResponse<ComparisonPlan>> CreatePlan([FromBody] ServiceInputModel.ComparisonPlanCreateInputModel input)
        {
            var moduleInput = await input.ToModuleInputModel(propertyService);
            var plan = await service.CreatePlanAsync(moduleInput);
            return new SingletonResponse<ComparisonPlan>(plan);
        }

        [HttpPatch("plan/{id:int}")]
        [SwaggerOperation(OperationId = "UpdateComparisonPlan")]
        public async Task<BaseResponse> UpdatePlan(int id, [FromBody] ServiceInputModel.ComparisonPlanPatchInputModel input)
        {
            var moduleInput = await input.ToModuleInputModel(propertyService);
            await service.UpdatePlanAsync(id, moduleInput);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("plan/{id:int}")]
        [SwaggerOperation(OperationId = "DeleteComparisonPlan")]
        public async Task<BaseResponse> DeletePlan(int id)
        {
            await service.DeletePlanAsync(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("plan/{id:int}/duplicate")]
        [SwaggerOperation(OperationId = "DuplicateComparisonPlan")]
        public async Task<SingletonResponse<ComparisonPlan>> DuplicatePlan(int id)
        {
            var plan = await service.DuplicatePlanAsync(id);
            return new SingletonResponse<ComparisonPlan>(plan);
        }

        #endregion

        #region Comparison Execution

        [HttpPost("plan/{id:int}/execute")]
        [SwaggerOperation(OperationId = "ExecuteComparisonPlan")]
        public async Task<SingletonResponse<string>> ExecutePlan(int id)
        {
            // Check if there's already a running task for this plan
            var existingTask = taskManager.GetTaskViewModel($"Comparison:{id}");
            if (existingTask?.Status == BTaskStatus.Running)
            {
                return new SingletonResponse<string>((int)ResponseCode.Conflict, "A comparison task for this plan is already running");
            }

            var taskId = await service.StartComparisonTaskAsync(id);
            return new SingletonResponse<string>(taskId);
        }

        #endregion

        #region Results

        [HttpGet("plan/{planId:int}/results")]
        [SwaggerOperation(OperationId = "SearchComparisonResults")]
        public async Task<ModuleView.ComparisonResultSearchResponse> SearchResults(
            int planId,
            [FromQuery] ComparisonResultSearchInputModel input)
        {
            var results = await service.SearchResultGroupsAsync(planId, input);
            return results;
        }

        [HttpGet("plan/{planId:int}/results/{groupId:int}")]
        [SwaggerOperation(OperationId = "GetComparisonResultGroup")]
        public async Task<SingletonResponse<ModuleView.ComparisonResultGroupViewModel?>> GetResultGroup(int planId, int groupId)
        {
            var group = await service.GetResultGroupAsync(groupId);
            return new SingletonResponse<ModuleView.ComparisonResultGroupViewModel?>(group);
        }

        [HttpGet("plan/{planId:int}/results/{groupId:int}/resource-ids")]
        [SwaggerOperation(OperationId = "GetComparisonResultGroupResourceIds")]
        public async Task<ListResponse<int>> GetResultGroupResourceIds(int planId, int groupId)
        {
            var resourceIds = await service.GetResultGroupResourceIdsAsync(groupId);
            return new ListResponse<int>(resourceIds);
        }

        [HttpGet("plan/{planId:int}/results/{groupId:int}/pairs")]
        [SwaggerOperation(OperationId = "GetComparisonResultGroupPairs")]
        public async Task<ModuleView.ComparisonResultPairsResponse> GetResultGroupPairs(
            int planId,
            int groupId,
            [FromQuery] int limit = 1000)
        {
            var pairs = await service.GetResultGroupPairsAsync(groupId, limit);
            return pairs;
        }

        [HttpPut("plan/{planId:int}/results/{groupId:int}/hide")]
        [SwaggerOperation(OperationId = "HideComparisonResultGroup")]
        public async Task<BaseResponse> HideResultGroup(int planId, int groupId)
        {
            await service.HideResultGroupAsync(groupId, true);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("plan/{planId:int}/results/{groupId:int}/hide")]
        [SwaggerOperation(OperationId = "UnhideComparisonResultGroup")]
        public async Task<BaseResponse> UnhideResultGroup(int planId, int groupId)
        {
            await service.HideResultGroupAsync(groupId, false);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("plan/{planId:int}/results")]
        [SwaggerOperation(OperationId = "ClearComparisonResults")]
        public async Task<BaseResponse> ClearResults(int planId)
        {
            await service.ClearResultsAsync(planId);
            return BaseResponseBuilder.Ok;
        }

        #endregion
    }
}
