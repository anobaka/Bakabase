using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Choice
{
    public abstract record ChoicePropertyOptions<TValue>
    {
        public List<ChoiceOptions>? Choices { get; set; }
        public bool AllowAddingNewOptionsWhileChoosing { get; set; }
        public TValue? DefaultValue { get; set; }

        public record ChoiceOptions
        {
            public string Id { get; set; } = null!;
            public string Value { get; set; } = null!;
            public string Color { get; set; } = null!;
        }
    }
}
