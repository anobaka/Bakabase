using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public abstract record JsonProcessValue<T>
    {
        public static T? Parse(string? serializedValue) => string.IsNullOrEmpty(serializedValue)
            ? default
            : JsonConvert.DeserializeObject<T>(serializedValue);

        public string Serialize() => JsonConvert.SerializeObject(this);
    }
}