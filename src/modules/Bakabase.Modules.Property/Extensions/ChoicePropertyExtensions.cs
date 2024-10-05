using Bakabase.Abstractions.Extensions;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Property.Extensions
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
        /// <param name="dbValues"></param>
        /// <returns>Whether options have been changed.</returns>
        public static bool AddChoices<T>(this ChoicePropertyOptions<T> options, bool ignoreSameValue,
            string[] values, string[]? dbValues)
        {
            if (options.AllowAddingNewDataDynamically)
            {
                var goodValues = values.TrimAndRemoveEmpty()?.ToArray();
                if (goodValues?.Any() == true)
                {
                    options.Choices ??= [];
                    if (ignoreSameValue)
                    {
                        var current = options.Choices.Select(c => c.Label).ToHashSet();
                        values = values.ToHashSet().Except(current).ToArray();
                    }

                    if (values.Any())
                    {
                        if (dbValues != null && dbValues.Length != values.Length)
                        {
                            throw new Exception(
                                $"Count of {nameof(values)} and {nameof(dbValues)} must be same if {nameof(dbValues)} is specified");
                        }

                        options.Choices.AddRange(values.Select((v, i) =>
                        {
                            var o = new ChoiceOptions
                            {
                                Label = v
                            };
                            var id = dbValues?[i];
                            if (id.IsNotEmpty())
                            {
                                o.Value = id;
                            }

                            return o;
                        }));
                        return true;
                    }
                }
            }

            return false;
        }
    }
}