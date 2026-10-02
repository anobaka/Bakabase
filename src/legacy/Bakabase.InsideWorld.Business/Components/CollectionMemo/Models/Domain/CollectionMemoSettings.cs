using System;

namespace Bakabase.InsideWorld.Business.Components.CollectionMemo.Models.Domain;

public class CollectionMemoSettings
{
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.IsoDateTimeConverter))]
    public DateTime StartAt { get; set; }
    public bool Reverse { get; set; }
}
