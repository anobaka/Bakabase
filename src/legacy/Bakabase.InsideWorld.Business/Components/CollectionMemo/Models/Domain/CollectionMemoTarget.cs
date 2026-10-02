using System;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;

public class CollectionMemoTarget
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public List<CollectionMemoRange> Ranges { get; set; } = [];
}

public class CollectionMemoRange
{
    public int Id { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime? StartAt { get; set; }
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime EndAt { get; set; }
}
