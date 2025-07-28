using System.Collections.Generic;
using Bakabase.Abstractions.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class TaskOptionsPatchInputModel
{
    public List<BTaskDbModel>? Tasks { get; set; }
}