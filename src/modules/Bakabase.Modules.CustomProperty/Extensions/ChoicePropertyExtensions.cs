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
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="options"></param>
        /// <param name="ignoreSameValue"></param>
        /// <param name="values"></param>
        /// <returns>Whether options have been changed.</returns>
        public static bool AddChoices<T>(this ChoicePropertyOptions<T> options, bool ignoreSameValue,
            params string[] values)
        {
            if (options.AllowAddingNewDataDynamically)
            {
                options.Choices ??= [];
                if (ignoreSameValue)
                {
                    var current = options.Choices.Select(c => c.Label).ToHashSet();
                    values = values.ToHashSet().Except(current).ToArray();
                }

                if (values.Any())
                {
                    options.Choices.AddRange(values.Select(v => new ChoicePropertyOptions<T>.ChoiceOptions
                    {
                        Value = Guid.NewGuid().ToString(),
                        Label = v
                    }));
                    return true;
                }
            }

            return false;
        }
    }
}