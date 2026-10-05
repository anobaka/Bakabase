using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public static class PostParserWorkflow
{
    public const string Trigger = "postParser.manual";
    public const string ReadContent = "postParser.readContent";
    public const string ExtractDownloadInfo = "postParser.extractDownloadInfo";
    public const string InputType = "item.postParser.input";
    public const string ContentType = "item.postParser.content";
    public const string ResultType = "item.postParser.result";
    public const string BuiltinName = "Parse post download information (v2)";
    public const string UnlockContent = "postParser.unlockContent";
    public const string CheckLinks = "postParser.checkLinks";

    public static IServiceCollection AddPostParserWorkflows<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddSingleton<PostParserTaskExecutionGate>();
        services.AddScoped<PostParserWorkflowService<TDbContext>>();
        services.AddScoped<IPostParserWorkflowTaskBridge>(sp => sp.GetRequiredService<PostParserWorkflowService<TDbContext>>());
        services.AddSingleton<IWorkflowTrigger, PostParserManualTrigger>();
        services.AddSingleton<IWorkflowActivity, ReadPostContentActivity>();
        services.AddSingleton<IWorkflowActivity, ExtractPostDownloadInfoActivity>();
        services.AddSingleton<IWorkflowActivity, UnlockPostContentActivity>();
        services.AddSingleton<IWorkflowActivity, CheckPostLinksActivity>();
        services.AddSingleton<IWorkflowItemTypeDescriptor>(new PostParserItemTypeDescriptor(InputType, "Post: link or text", typeof(PostParserInput)));
        services.AddSingleton<IWorkflowItemTypeDescriptor>(new PostParserItemTypeDescriptor(ContentType, "Post: readable content", typeof(PostParserContentItem)));
        services.AddSingleton<IWorkflowItemTypeDescriptor>(new PostParserItemTypeDescriptor(ResultType, "Post: download information", typeof(PostParserResultItem)));
        return services;
    }
}

/// <summary>The task reference is checked against the persisted run; manual input cannot impersonate a task.</summary>
public sealed record PostParserInput
{
    public string? Link { get; init; }
    public string? Text { get; init; }
    public string? Title { get; init; }
    public string? SourceHint { get; init; }
    public int? TaskId { get; init; }
    public int Revision { get; init; }
}

public sealed record PostParserContentItem(PostParserInput Input, PostContent Content)
{
    public PostAvailabilityAssessment? Availability { get; init; }
    public List<string> Warnings { get; init; } = [];
}
public sealed record PostParserResultItem(PostParserInput Input, PostDownloadInfo Result);
public sealed record PostParserItemTypeDescriptor(string ItemType, string DisplayName, Type ClrType) : IWorkflowItemTypeDescriptor;
public sealed class PostParserTaskExecutionGate { public SemaphoreSlim Semaphore { get; } = new(1, 1); }

public interface IPostParserWorkflowTaskBridge
{
    Task EnsureCurrentAsync(PostParserInput input, int runId, CancellationToken ct);
    Task SaveResultAsync(PostParserInput input, int runId, PostDownloadInfo result, CancellationToken ct);
    Task SaveSnapshotAsync(PostParserInput input, int runId, PostContent content,
        PostAvailabilityAssessment? availability, string state, string? message, CancellationToken ct) => Task.CompletedTask;
}

public sealed class PostParserManualTrigger : IWorkflowTrigger
{
    public string Kind => PostParserWorkflow.Trigger;
    public string DisplayName => "Parse a post or text";
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.Manual;
    public string SourceModule => "postParser";
    public string Description => "Run manually with one supported post link or pasted text. Saved post-parser tasks start their own linked run from the post parser. This is not a broadcast subscription and parsing alone creates no resource or download task.";
    public string DescriptionKey => "workflow.trigger.postParserManual.description";
    public Type PayloadType => typeof(PostParserInput);
    public bool Matches(object payload, string? triggerFilterJson) => false;
    public string ResolveOutputItemType(string? triggerFilterJson) => PostParserWorkflow.InputType;
    public IReadOnlyList<object> ExtractItems(object payload)
    {
        var input = payload as PostParserInput ?? throw new InvalidOperationException("Provide a post link or text.");
        Validate(input);
        return [input];
    }

    public object BuildManualPayload(string? triggerFilterJson, string? argsJson)
    {
        var input = JsonSerializer.Deserialize<PostParserInput>(argsJson ?? "{}", WorkflowJson.Options)
            ?? throw new InvalidOperationException("Provide a post link or text.");
        if (input.TaskId != null) throw new InvalidOperationException("Start saved parsing tasks from the post parser.");
        Validate(input);
        return input;
    }

    public static void Validate(PostParserInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Link) == string.IsNullOrWhiteSpace(input.Text))
            throw new InvalidOperationException("Provide either one link or one text, not both.");
        if (!string.IsNullOrWhiteSpace(input.Link) &&
            (!Uri.TryCreate(input.Link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("A post link must be an HTTP or HTTPS URL.");
    }
}

public sealed class ReadPostContentActivity : IWorkflowActivity
{
    public string Kind => PostParserWorkflow.ReadContent;
    public string DisplayName => "Read post content";
    public string Description => "Read and save the first page or pasted text, including replies and restricted items, before analysis or purchases.";
    public string DescriptionKey => "workflow.activity.postParserReadContent.description";
    public string Group => "postParser";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public IReadOnlyList<string> AcceptedInputItemTypes => [PostParserWorkflow.InputType];
    public WorkflowItemTypeBehavior OutputBehavior => WorkflowItemTypeBehavior.Fixed;
    public string FixedOutputItemType => PostParserWorkflow.ContentType;
    public sealed record Config { public bool UseConfiguredSoulPlusPurchaseLimit { get; init; } }

    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateConfigAsync(WorkflowValidationContext context, CancellationToken ct)
    {
        if (context.Payload is not PostParserInput input)
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([]);
        try
        {
            PostParserManualTrigger.Validate(input);
            if (input.Link is {Length: > 0} link && !context.Services.GetRequiredService<IPostContentService>().CanRead(link, input.SourceHint))
                throw new InvalidOperationException("No registered content reader supports this link.");
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([]);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>([new() {Code = "postParser.inputInvalid", Message = ex.Message}]);
        }
    }

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        var input = item as PostParserInput ?? throw new InvalidOperationException("This node needs a post link or text.");
        PostParserManualTrigger.Validate(input);
        var bridge = ctx.Services.GetRequiredService<IPostParserWorkflowTaskBridge>();
        var runId = checked((int)ctx.RunId);
        await bridge.EnsureCurrentAsync(input, runId, ct);
        var service = ctx.Services.GetRequiredService<IPostContentService>();
        var content = input.Text is {Length: > 0} text
            ? new PostContent {Title = input.Title ?? "", MainHtml = text, SourceHint = input.SourceHint}
            : await service.ReadAsync(input.Link!, input.SourceHint, ct);
        await bridge.EnsureCurrentAsync(input, runId, ct);
        await bridge.SaveSnapshotAsync(input, runId, content, null, "snapshotSaved", null, ct);
        return WorkflowItemOutcome.ReplaceWith(new PostParserContentItem(input, content));
    }
}
