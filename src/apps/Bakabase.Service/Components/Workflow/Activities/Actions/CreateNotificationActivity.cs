using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Generic side-effect action: write a persistent notification per item, with title and
/// body strings that may interpolate <c>{{field}}</c> references against the item's JSON
/// shape. Accepts any input item type and passes the item through unchanged.
/// </summary>
public class CreateNotificationActivity : IWorkflowActivity
{
    public string Kind { get; } = NotificationWorkflowActivityKinds.Create;
    public string DisplayName => "Send notification";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Notification;
    // No AcceptedInputItemTypes / OutputBehavior overrides: accepts any, passthrough.

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Matches {{field}} or {{outer.inner}} — restricted to identifier-ish characters so a
    // stray "{{" in user text doesn't gobble the rest of the template.
    private static readonly Regex Placeholder = new(@"\{\{([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*)\}\}",
        RegexOptions.Compiled);

    public async Task<WorkflowItemOutcome> ProcessItemAsync(
        WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var cfg = ctx.GetConfig<Config>() ?? new Config();
        var notifications = ctx.Services.GetRequiredService<INotificationService>();

        var itemJson = JsonSerializer.SerializeToDocument(item, JsonOptions);
        var title = Render(cfg.Title, itemJson.RootElement);
        var body = string.IsNullOrWhiteSpace(cfg.Body) ? null : Render(cfg.Body, itemJson.RootElement);

        if (string.IsNullOrWhiteSpace(title))
            title = "Workflow notification";

        await notifications.CreateAsync(new NotificationCreationInputModel
        {
            Source = $"workflow:{ctx.WorkflowDefinitionId}",
            Title = title,
            Body = body,
            Severity = cfg.Severity,
        });

        return WorkflowItemOutcome.KeepItem;
    }

    private static string Render(string template, JsonElement itemRoot)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return Placeholder.Replace(template, match =>
        {
            var path = match.Groups[1].Value.Split('.');
            var node = itemRoot;
            foreach (var seg in path)
            {
                if (node.ValueKind != JsonValueKind.Object) return match.Value;
                if (!node.TryGetProperty(seg, out var child)) return match.Value;
                node = child;
            }
            return node.ValueKind switch
            {
                JsonValueKind.Null => "",
                JsonValueKind.String => node.GetString() ?? "",
                _ => node.ToString(),
            };
        });
    }

    private sealed record Config
    {
        /// <summary>Required template. Use <c>{{fieldName}}</c> to interpolate item fields.</summary>
        public string Title { get; init; } = "";

        /// <summary>Optional template; same interpolation rules.</summary>
        public string? Body { get; init; }

        public AppNotificationSeverity Severity { get; init; } = AppNotificationSeverity.Info;
    }
}
