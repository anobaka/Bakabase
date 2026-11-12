using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

public sealed record AcquisitionHandoffView(string Id, WorkflowSourceRef Source, string Status,
    string? Code, int? AcquisitionTaskId,
    [property: Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))] DateTime UpdatedAt)
{
    public static AcquisitionHandoffView From(AcquisitionHandoffDbModel row) => new(row.Id,
        new(row.SourceModule, row.SourceKind, row.SourceId, row.SourceRevision), row.Status,
        row.Code, row.AcquisitionTaskId, DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc));
}
