using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class TranslationTool(IAiTranslationService translationService) : ILlmTool
{
    [Description("Translate text from one language to another using the configured AI translation service.")]
    public async Task<string> TranslateText(
        [Description("The text to translate")] string text,
        [Description("Target language (e.g., 'Chinese', 'English', 'Japanese')")] string targetLanguage,
        [Description("Source language (e.g., 'Chinese', 'English'). Use 'auto' for auto-detect.")] string sourceLanguage)
    {
        try
        {
            var effectiveSource = string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase) ? null : sourceLanguage;
            var result = await translationService.TranslateAsync(text, targetLanguage, effectiveSource);
            return JsonSerializer.Serialize(new { Success = true, Translation = result });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = ex.Message });
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(TranslateText);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "TranslateText", Description = "Translate text between languages", IsReadOnly = true };
    }
}
