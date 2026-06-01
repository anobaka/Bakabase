using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("workflow")]
public class WorkflowController(
    IWorkflowDefinitionService service,
    IWorkflowTriggerRegistry triggers,
    IWorkflowActivityRegistry activities,
    IWorkflowItemTypeRegistry itemTypes) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "SearchWorkflows")]
    public async Task<ListResponse<WorkflowDefinitionViewModel>> Search([FromQuery] WorkflowDefinitionSearchInputModel model)
    {
        var rows = await service.SearchAsync(model);
        return new ListResponse<WorkflowDefinitionViewModel>(
            rows.Select(WorkflowDefinitionViewModel.From));
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetWorkflow")]
    public async Task<SingletonResponse<WorkflowDefinitionViewModel?>> Get(int id)
    {
        var row = await service.GetAsync(id);
        return new SingletonResponse<WorkflowDefinitionViewModel?>(
            row is null ? null : WorkflowDefinitionViewModel.From(row));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddWorkflow")]
    public async Task<SingletonResponse<WorkflowDefinitionViewModel>> Add(
        [FromBody] WorkflowDefinitionCreationInputModel model, CancellationToken ct)
    {
        var row = await service.CreateAsync(model, ct);
        return new SingletonResponse<WorkflowDefinitionViewModel>(WorkflowDefinitionViewModel.From(row));
    }

    [HttpPatch("{id:int}")]
    [SwaggerOperation(OperationId = "PatchWorkflow")]
    public async Task<SingletonResponse<WorkflowDefinitionViewModel>> Patch(
        int id, [FromBody] WorkflowDefinitionUpdateInputModel model, CancellationToken ct)
    {
        var row = await service.UpdateAsync(id, model, ct);
        return new SingletonResponse<WorkflowDefinitionViewModel>(WorkflowDefinitionViewModel.From(row));
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteWorkflow")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.DeleteAsync(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("triggers")]
    [SwaggerOperation(OperationId = "GetWorkflowTriggers")]
    public ListResponse<WorkflowTriggerDescriptorViewModel> GetTriggers()
    {
        return new ListResponse<WorkflowTriggerDescriptorViewModel>(triggers.All.Select(t =>
            new WorkflowTriggerDescriptorViewModel { Kind = t.Kind, DisplayName = t.DisplayName }));
    }

    [HttpGet("activities")]
    [SwaggerOperation(OperationId = "GetWorkflowActivities")]
    public ListResponse<WorkflowActivityDescriptorViewModel> GetActivities()
    {
        // Return all activities with their item-type metadata; the editor walks the chain
        // client-side to decide which are addable at each position.
        return new ListResponse<WorkflowActivityDescriptorViewModel>(activities.All.Select(a =>
            new WorkflowActivityDescriptorViewModel
            {
                Kind = a.Kind,
                DisplayName = a.DisplayName,
                Category = a.Category,
                Group = a.Group,
                AcceptedInputItemTypes = a.AcceptedInputItemTypes.ToList(),
                OutputBehavior = a.OutputBehavior,
                FixedOutputItemType = a.FixedOutputItemType,
            }));
    }

    [HttpGet("item-types")]
    [SwaggerOperation(OperationId = "GetWorkflowItemTypes")]
    public ListResponse<WorkflowItemTypeDescriptorViewModel> GetItemTypes()
    {
        return new ListResponse<WorkflowItemTypeDescriptorViewModel>(
            itemTypes.All.Select(BuildItemTypeVm));
    }

    private static WorkflowItemTypeDescriptorViewModel BuildItemTypeVm(IWorkflowItemTypeDescriptor d)
    {
        var fields = d.ClrType
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => new WorkflowItemTypeFieldViewModel
            {
                Name = JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                Type = FriendlyTypeName(p.PropertyType),
                Nullable = IsNullableProperty(p),
            })
            .ToList();
        return new WorkflowItemTypeDescriptorViewModel
        {
            ItemType = d.ItemType,
            DisplayName = d.DisplayName,
            Fields = fields,
        };
    }

    private static string FriendlyTypeName(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t) ?? t;
        if (underlying.IsArray) return FriendlyTypeName(underlying.GetElementType()!) + "[]";
        if (underlying.IsGenericType)
        {
            var args = string.Join(", ", underlying.GetGenericArguments().Select(FriendlyTypeName));
            var name = underlying.Name;
            var tickIdx = name.IndexOf('`');
            if (tickIdx > 0) name = name[..tickIdx];
            return $"{name}<{args}>";
        }
        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(int)) return "int";
        if (underlying == typeof(long)) return "long";
        if (underlying == typeof(bool)) return "bool";
        if (underlying == typeof(decimal)) return "decimal";
        if (underlying == typeof(double)) return "double";
        if (underlying == typeof(DateTime)) return "DateTime";
        return underlying.Name;
    }

    private static bool IsNullableProperty(System.Reflection.PropertyInfo p)
    {
        if (Nullable.GetUnderlyingType(p.PropertyType) != null) return true;
        if (p.PropertyType.IsValueType) return false;
        var ctx = new System.Reflection.NullabilityInfoContext();
        return ctx.Create(p).ReadState == System.Reflection.NullabilityState.Nullable;
    }

    [HttpGet("{id:int}/runs")]
    [SwaggerOperation(OperationId = "SearchWorkflowRuns")]
    public async Task<SearchResponse<WorkflowRunViewModel>> SearchRuns(int id, [FromQuery] WorkflowRunSearchInputModel model)
    {
        model.WorkflowDefinitionId = id;
        var result = await service.SearchRunsAsync(model);
        return new SearchResponse<WorkflowRunViewModel>(
            result.Data!.Select(WorkflowRunViewModel.From),
            result.TotalCount, result.PageIndex, result.PageSize);
    }
}
