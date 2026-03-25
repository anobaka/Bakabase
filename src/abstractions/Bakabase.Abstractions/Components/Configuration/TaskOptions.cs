using Bakabase.Abstractions.Models.Db;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Abstractions.Components.Configuration;

[Options(fileKey: "task")]
public class TaskOptions
{
    public List<BTaskDbModel>? Tasks { get; set; }
}