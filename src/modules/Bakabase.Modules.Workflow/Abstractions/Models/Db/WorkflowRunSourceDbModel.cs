using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Db;

public sealed record WorkflowRunSourceDbModel
{
    [Key] public int Id { get; set; }
    public int WorkflowRunId { get; set; }
    [MaxLength(100)] public required string Module { get; set; }
    [MaxLength(100)] public required string EntityKind { get; set; }
    [MaxLength(500)] public required string EntityId { get; set; }
    [MaxLength(100)] public string Revision { get; set; } = "";
    [MaxLength(500)] public string ResultKey { get; set; } = "";

    public WorkflowSourceRef ToSourceRef() => new(Module, EntityKind, EntityId, Revision, ResultKey);

    public static List<WorkflowRunSourceDbModel> From(object payload) => payload is IWorkflowSourcePayload sourced
        ? sourced.SourceRefs.Distinct().Select(s =>
        {
            if (string.IsNullOrWhiteSpace(s.Module) || s.Module.Length > 100 ||
                string.IsNullOrWhiteSpace(s.EntityKind) || s.EntityKind.Length > 100 ||
                string.IsNullOrWhiteSpace(s.EntityId) || s.EntityId.Length > 500 ||
                s.Revision?.Length > 100 || s.ResultKey?.Length > 500)
                throw new ArgumentException("Invalid workflow source reference.");
            return new WorkflowRunSourceDbModel {Module = s.Module, EntityKind = s.EntityKind,
                EntityId = s.EntityId, Revision = s.Revision ?? "", ResultKey = s.ResultKey ?? ""};
        }).ToList() : [];
}
