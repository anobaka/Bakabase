using System;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Workflow;

public sealed class PostParserWorkflowSchedulingPolicy(IBOptions<ThirdPartyOptions> options)
    : IWorkflowRunSchedulingPolicy
{
    public bool AppliesTo(string triggerKind) => triggerKind == PostParserWorkflow.Trigger;
    public bool ResumeOnStartup => false;
    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) => builder
        .WithConcurrencyLimit("postParser", () => Math.Max(1, options.Value.PostParserMaxConcurrency));
}
