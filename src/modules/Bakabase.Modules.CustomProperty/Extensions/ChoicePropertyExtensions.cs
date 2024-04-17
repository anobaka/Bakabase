using Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.CustomProperty.Extensions
{
    public static class ChoicePropertyExtensions
    {
        public static void AddChoices<T>(this ChoicePropertyOptions<T> options, bool ignoreSameValue, params string[] values)
        {
            options.Choices ??= new List<ChoicePropertyOptions<T>.ChoiceOptions>();

            if (ignoreSameValue)
            {
                var current = options.Choices.Select(c => c.Value).ToHashSet();
                values = values.ToHashSet().Except(current).ToArray();
            }

            options.Choices.AddRange(values.Select(v => new ChoicePropertyOptions<T>.ChoiceOptions
            {
                Id = Guid.NewGuid().ToString(),
                Value = v
            }));
        }
    }
}