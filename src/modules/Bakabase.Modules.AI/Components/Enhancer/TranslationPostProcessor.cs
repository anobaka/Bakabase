using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.StandardValue;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Enhancer;

/// <summary>
/// Post-processes enhancement results by translating property values.
/// Activated per-enhancer when translation is enabled in enhancer options.
/// Uses the Translation AI feature configuration (not per-enhancer provider/model).
/// </summary>
public class TranslationPostProcessor(
    IAiTranslationService translationService,
    ILogger<TranslationPostProcessor> logger
) : IEnhancementPostProcessor
{
    /// <summary>
    /// Value types that should not be translated.
    /// </summary>
    private static readonly HashSet<StandardValueType> UntranslatableTypes =
    [
        StandardValueType.Boolean,
        StandardValueType.DateTime,
        StandardValueType.Time,
        StandardValueType.Decimal
    ];

    public async Task ProcessAsync(EnhancementPostProcessorContext context, CancellationToken ct)
    {
        var translationOptions = context.TranslationOptions;
        if (!translationOptions.Enabled)
            return;

        var targetLanguage = translationOptions.TargetLanguage;
        if (string.IsNullOrWhiteSpace(targetLanguage))
            return;

        // Collect all translatable values: convert each to List<string>, translate, convert back
        var translatableValues = context.Values
            .Where(v => v.Value != null && !UntranslatableTypes.Contains(v.ValueType))
            .ToList();

        if (translatableValues.Count == 0) return;

        // Step 1: Convert all values to List<string> for translation
        var valueStringLists = new List<(EnhancementValue Value, List<string> Strings)>();

        foreach (var ev in translatableValues)
        {
            var strings = ConvertToStringList(ev.Value, ev.ValueType);
            if (strings != null && strings.Count > 0)
            {
                valueStringLists.Add((ev, strings));
            }
        }

        if (valueStringLists.Count == 0) return;

        // Step 2: Flatten all strings for batch translation
        var allTexts = valueStringLists
            .SelectMany(v => v.Strings)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (allTexts.Count == 0) return;

        try
        {
            var result = await translationService.TranslateBatchAsync(
                allTexts,
                targetLanguage,
                ct: ct);

            // Step 3: Map translations back and convert to original types
            var translationIndex = 0;

            foreach (var (ev, strings) in valueStringLists)
            {
                var translatedStrings = new List<string>();

                foreach (var s in strings)
                {
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        translatedStrings.Add(s);
                    }
                    else if (translationIndex < result.Results.Count)
                    {
                        translatedStrings.Add(result.Results[translationIndex].TranslatedText);
                        translationIndex++;
                    }
                    else
                    {
                        translatedStrings.Add(s);
                    }
                }

                // Convert translated strings back to original type
                ev.Value = ConvertFromStringList(translatedStrings, ev.ValueType);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Translation post-processing failed for resource {ResourceId}, enhancer {EnhancerId}",
                context.ResourceId, context.EnhancerId);
        }
    }

    /// <summary>
    /// Convert a value to List&lt;string&gt; for translation.
    /// </summary>
    private static List<string>? ConvertToStringList(object? value, StandardValueType valueType)
    {
        if (value == null) return null;

        if (valueType == StandardValueType.ListString)
        {
            return value as List<string>;
        }

        // Use StandardValueSystem to convert to ListString
        var converted = StandardValueSystem.Convert(value, valueType, StandardValueType.ListString);
        return converted as List<string>;
    }

    /// <summary>
    /// Convert translated List&lt;string&gt; back to the original value type.
    /// </summary>
    private static object? ConvertFromStringList(List<string> strings, StandardValueType targetType)
    {
        if (targetType == StandardValueType.ListString)
        {
            return strings;
        }

        // Use StandardValueSystem to convert from ListString back to original type
        return StandardValueSystem.Convert(strings, StandardValueType.ListString, targetType);
    }
}
