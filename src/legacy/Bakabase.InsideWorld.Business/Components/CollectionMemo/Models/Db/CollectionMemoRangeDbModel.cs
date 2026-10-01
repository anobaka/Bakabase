using System;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;

public class CollectionMemoRangeDbModel
{
    public int Id { get; set; }
    public int TargetId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
}
