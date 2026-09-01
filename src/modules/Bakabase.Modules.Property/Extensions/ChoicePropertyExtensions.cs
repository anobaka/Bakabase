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
            // Validate before any filtering so a caller-side mismatch is always surfaced,
            // and so label/id pairs stay aligned through the trim/dedupe steps below.
            if (dbValues != null && dbValues.Length != values.Length)
            {
                throw new Exception(
                    $"Count of {nameof(values)} and {nameof(dbValues)} must be same if {nameof(dbValues)} is specified");
            }

            var candidates = values
                .Select((v, i) => (Label: v?.Trim(), DbValue: dbValues?[i]))
                .Where(x => !string.IsNullOrEmpty(x.Label))
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            options.Choices ??= [];
            if (ignoreSameValue)
            {
                // Seed with existing labels so duplicates are skipped; Add() also
                // de-duplicates repeated labels inside this call.
                var seen = options.Choices.Select(c => c.Label).ToHashSet();
                candidates = candidates.Where(x => seen.Add(x.Label!)).ToList();
                if (candidates.Count == 0)
                {
                    return false;
                }
            }

            options.Choices.AddRange(candidates.Select(x => x.DbValue.IsNotEmpty()
                ? new ChoiceOptions {Label = x.Label!, Value = x.DbValue!}
                : new ChoiceOptions {Label = x.Label!}));
            return true;
        }
    }
}
