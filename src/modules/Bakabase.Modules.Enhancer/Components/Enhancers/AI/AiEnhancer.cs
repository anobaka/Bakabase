using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.AI;

public class AiEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    ILlmService llmService,
    IBOptionsManager<AppOptions> appOptionsManager,
    IServiceProvider serviceProvider
) : AbstractEnhancer<AiEnhancerTarget, AiEnhancerContext, EnhancerFullOptions>(loggerFactory, fileManager, serviceProvider)
{
    protected override EnhancerId TypedId => EnhancerId.AI;

    protected override async Task<AiEnhancerContext?> BuildContextInternal(
        Resource resource, EnhancerFullOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var fileName = Path.GetFileNameWithoutExtension(resource.Path);
        var directoryName = Path.GetFileName(Path.GetDirectoryName(resource.Path));

        logCollector.LogInfo(EnhancementLogEvent.DataFetching,
            $"Analyzing resource with AI: {fileName}",
            new { FileName = fileName, DirectoryName = directoryName, Path = resource.Path });

        var language = appOptionsManager.Value?.Language;
        var systemPrompt = BuildSystemPrompt(options, language);
        var userPrompt = BuildUserPrompt(fileName, directoryName, resource.Path);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        ChatResponse response;
        try
        {
            response = await llmService.CompleteForFeatureAsync(AiFeature.Enhancer, messages, ct: ct);
        }
        catch (Exception ex)
        {
            logCollector.LogError(EnhancementLogEvent.Error,
                $"AI call failed: {ex.Message}",
                new { ExceptionType = ex.GetType().Name, ex.Message });
            throw;
        }

        var responseText = response.Text?.Trim();
        if (string.IsNullOrEmpty(responseText))
        {
            logCollector.LogWarning(EnhancementLogEvent.DataFetched, "AI returned empty response");
            return null;
        }

        logCollector.LogInfo(EnhancementLogEvent.DataFetched,
            "AI response received",
            new { ResponseLength = responseText.Length });

        var context = ParseAiResponse(responseText, logCollector);
        return context;
    }

    protected override Task<List<EnhancementTargetValue<AiEnhancerTarget>>> ConvertContextByTargets(
        AiEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<AiEnhancerTarget>>();

        foreach (var (key, values) in context.Properties)
        {
            if (values.Count > 0)
            {
                enhancements.Add(new EnhancementTargetValue<AiEnhancerTarget>(
                    AiEnhancerTarget.Properties, key,
                    new ListStringValueBuilder(values)));
            }
        }

        return Task.FromResult(enhancements);
    }

    private static string BuildSystemPrompt(EnhancerFullOptions options, string? language)
    {
        var customPromptSection = BuildCustomPromptSection(options);
        var dynamicTargetNames = GetUserDefinedDynamicTargetNames(options);
        var languageInstruction = BuildLanguageInstruction(language, dynamicTargetNames);

        return $$"""
            You are a media file metadata analyzer. Given a file path and name, extract as much metadata as possible.
            Respond ONLY with a JSON object (no markdown fences, no extra text).
            Each key is a property name, each value is an array of strings.
            Example:
            {
              "Name": ["Clean Title"],
              "Tags": ["Action", "Sci-Fi"],
              "Series": ["Series Name"],
              "Publisher": ["Publisher Name"],
              "Language": ["Japanese"],
              "ReleaseDate": ["2019-12-02"]
            }
            Rules:
            - Extract the clean title from the filename, removing brackets, tags, and technical info
            - Identify tags like genre, language, quality, format from filename patterns
            - If a release date is embedded in the filename (e.g., [20191202]), extract it
            - Only include properties you can confidently extract
            - Do NOT guess or fabricate information
            - All values must be string arrays, even for single values
            {{customPromptSection}}{{languageInstruction}}
            """;
    }

    private static List<string> GetUserDefinedDynamicTargetNames(EnhancerFullOptions options)
    {
        return options.TargetOptions?
            .Where(t => !string.IsNullOrWhiteSpace(t.DynamicTarget))
            .Select(t => t.DynamicTarget!)
            .Distinct()
            .ToList() ?? [];
    }

    private static string BuildLanguageInstruction(string? language, List<string> dynamicTargetNames)
    {
        if (string.IsNullOrEmpty(language))
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"IMPORTANT: For auto-discovered property names and their values, you must respond in {language}.");

        if (dynamicTargetNames.Count > 0)
        {
            var namesList = string.Join(", ", dynamicTargetNames.Select(n => $"\"{n}\""));
            sb.AppendLine($"CRITICAL: The following property names are user-defined and must be used EXACTLY as-is: {namesList}.");
            sb.AppendLine("Do NOT translate, rename, or modify these property names in any way.");
            sb.AppendLine("Do NOT change the language of the values extracted for these user-defined properties. Preserve their original language as found in the source.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildCustomPromptSection(EnhancerFullOptions options)
    {
        var targetOptions = options.TargetOptions?
            .Where(t => !string.IsNullOrWhiteSpace(t.CustomPrompt) && !string.IsNullOrWhiteSpace(t.DynamicTarget))
            .ToList();

        if (targetOptions == null || targetOptions.Count == 0)
            return string.Empty;

        var lines = targetOptions
            .Select(t => $"- For \"{t.DynamicTarget}\": {t.CustomPrompt!.Trim()}")
            .ToList();

        return "\nAdditional instructions for specific properties:\n" + string.Join("\n", lines);
    }

    private static string BuildUserPrompt(string? fileName, string? directoryName, string fullPath)
    {
        return $"Analyze this media file:\nFilename: {fileName}\nDirectory: {directoryName}\nFull path: {fullPath}";
    }

    private AiEnhancerContext? ParseAiResponse(string responseText, EnhancementLogCollector logCollector)
    {
        var json = responseText;
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var context = new AiEnhancerContext();

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var values = prop.Value.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()!)
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .ToList();
                    if (values.Count > 0)
                        context.Properties[prop.Name] = values;
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                        context.Properties[prop.Name] = [val];
                }
            }

            logCollector.LogInfo(EnhancementLogEvent.ContextBuilt,
                $"Parsed AI response: {context.Properties.Count} properties extracted");

            return context;
        }
        catch (JsonException ex)
        {
            logCollector.LogError(EnhancementLogEvent.Error,
                $"Failed to parse AI response as JSON: {ex.Message}",
                new { ResponseText = responseText[..Math.Min(200, responseText.Length)] });
            Logger.LogWarning(ex, "Failed to parse AI response: {Response}", responseText);
            return null;
        }
    }
}
