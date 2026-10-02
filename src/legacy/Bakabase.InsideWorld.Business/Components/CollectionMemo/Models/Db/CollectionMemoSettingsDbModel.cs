using System;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Db;

public class CollectionMemoSettingsDbModel
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public DateTime StartAt { get; set; }
    public bool Reverse { get; set; } = true;
}
