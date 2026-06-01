using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms;

/// <summary>
/// Generic LLM bridge. Knows nothing about specific sources — its job is to take an item
/// of the type flowing in and produce an item of the type the chain needs next.
///
/// <para><b>Output type resolution</b>: configure <c>TargetItemType</c> explicitly, or leave
/// blank to inherit from the next activity's single accepted type. If the next activity
/// accepts multiple types (or "any"), the user MUST pin TargetItemType — the editor flags
/// the activity invalid otherwise.</para>
///
/// <para><b>Prompt</b>: built from the source + target item type descriptors (reflected
/// field lists) so the LLM knows what shape to produce, plus any user-supplied extra
/// instructions. Response is parsed as JSON and deserialized into the target's CLR type.</para>
/// </summary>
public class AiTransformActivity : IWorkflowActivity
{
    public string Kind { get; } = AiWorkflowActivityKinds.Transform;
    public string DisplayName => "AI transform";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Ai;
    public IReadOnlyList<string> AcceptedInputItemTypes => [];  // any
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.AdaptToNext;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string? ResolveAdaptedOutputType(string configJson, string? nextActivityRequiredType)
    {
        var cfg = ParseConfig(configJson);
        return !string.IsNullOrWhiteSpace(cfg.TargetItemType) ? cfg.TargetItemType : nextActivityRequiredType;
    }

    public async Task<WorkflowItemOutcome> ProcessItemAsync(
        WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var targetType = ctx.TargetItemType;
        if (string.IsNullOrEmpty(targetType))
        {
            // Should have been caught at save time; bail loudly to surface the bug.
            throw new InvalidOperationException("AI transform missing TargetItemType");
        }

        var registry = ctx.Services.GetRequiredService<IWorkflowItemTypeRegistry>();
        var llm = ctx.Services.GetRequiredService<ILlmService>();
        var cfg = ParseConfig(ctx.ActivityConfigJson);

        var targetDescriptor = registry.Get(targetType)
            ?? throw new InvalidOperationException($"Unknown target item type: {targetType}");
        // Source descriptor is best-effort — we use it only for prompt context.
        IWorkflowItemTypeDescriptor? sourceDescriptor = null;
        // The runner doesn't pass the source type tag explicitly; infer from the item's CLR
        // type by reverse-lookup. This is fuzzy when multiple item types share a CLR type
        // (e.g. SubscriptionItem), but the prompt will still include the shape — the source
        // semantics are described by the user's extra instructions.
        sourceDescriptor = registry.All.FirstOrDefault(d => d.ClrType == item.GetType());

        var prompt = BuildPrompt(item, sourceDescriptor, targetDescriptor, cfg.ExtraInstructions);
        var response = await llm.CompleteForFeatureAsync(
            AiFeature.Default,
            [new(ChatRole.System, prompt.System), new(ChatRole.User, prompt.User)],
            new LlmModelParameters { UseJsonResponseFormat = true },
            ct);

        var text = response.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Logger.LogWarning("AI transform returned empty output (target {TargetType})", targetType);
            return WorkflowItemOutcome.DropItem;
        }

        object? produced;
        try
        {
            produced = JsonSerializer.Deserialize(text, targetDescriptor.ClrType, JsonOptions);
        }
        catch (JsonException ex)
        {
            ctx.Logger.LogWarning(ex,
                "AI transform output didn't parse as {TargetType} (raw: {Raw})", targetType, text);
            return WorkflowItemOutcome.DropItem;
        }
        if (produced is null) return WorkflowItemOutcome.DropItem;

        return WorkflowItemOutcome.ReplaceWith(produced);
    }

    private static (string System, string User) BuildPrompt(
        object inputItem,
        IWorkflowItemTypeDescriptor? source,
        IWorkflowItemTypeDescriptor target,
        string? extraInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "You convert workflow items between typed shapes. Read the input JSON, then " +
            "output a SINGLE JSON object that matches the target shape exactly. Use only " +
            "the fields listed. Do not wrap in markdown, prose, or extra keys.");
        sb.AppendLine();
        if (source is not null)
            sb.AppendLine($"Source type: {source.DisplayName} (\"{source.ItemType}\")");
        sb.AppendLine($"Target type: {target.DisplayName} (\"{target.ItemType}\")");
        sb.AppendLine();
        sb.AppendLine("Target shape (camelCase JSON keys):");
        sb.AppendLine(DescribeShape(target.ClrType));
        if (!string.IsNullOrWhiteSpace(extraInstructions))
        {
            sb.AppendLine();
            sb.AppendLine("Additional instructions:");
            sb.AppendLine(extraInstructions);
        }

        var inputJson = JsonSerializer.Serialize(inputItem, JsonOptions);
        return (sb.ToString(), inputJson);
    }

    private static string DescribeShape(Type t)
    {
        var lines = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p =>
                $"  - {JsonNamingPolicy.CamelCase.ConvertName(p.Name)}: {FriendlyType(p.PropertyType)}");
        return "{\n" + string.Join("\n", lines) + "\n}";
    }

    private static string FriendlyType(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return "string";
        if (u == typeof(int) || u == typeof(long)) return "number";
        if (u == typeof(bool)) return "boolean";
        if (u.IsArray) return FriendlyType(u.GetElementType()!) + "[]";
        return u.Name;
    }

    private static Config ParseConfig(string json)
    {
        if (string.IsNullOrEmpty(json)) return new Config();
        try { return JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config(); }
        catch (JsonException) { return new Config(); }
    }

    private sealed record Config
    {
        /// <summary>Optional pinned target item type. When null, inherits from the next step.</summary>
        public string? TargetItemType { get; init; }

        /// <summary>Optional user-supplied guidance appended to the system prompt.</summary>
        public string? ExtraInstructions { get; init; }
    }
}
