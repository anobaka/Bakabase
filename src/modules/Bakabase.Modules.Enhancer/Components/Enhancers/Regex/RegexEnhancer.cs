using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Resource = Bakabase.Abstractions.Models.Domain.Resource;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Regex;

[EnhancerComponent(OptionsType = typeof(RegexEnhancerOptions))]
public class RegexEnhancer(
    ILoggerFactory loggerFactory,
    IFileManager fileManager,
    IBOptions<EnhancerOptions> enhancerOptions,
    IServiceProvider serviceProvider)
    : AbstractEnhancer<RegexEnhancerTarget, RegexEnhancerContext, IRegexEnhancerOptions>(loggerFactory, fileManager, serviceProvider)
{
    protected override async Task<RegexEnhancerContext?> BuildContextInternal(Resource resource, IRegexEnhancerOptions options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var expressions = options.Expressions ?? enhancerOptions.Value.RegexEnhancer?.Expressions ?? [];
        if (!expressions.Any())
        {
            logCollector.LogWarning(EnhancementLogEvent.Configuration,
                "No regex expressions configured",
                new { Source = options.Expressions != null ? "Options" : "GlobalConfig" });
            return null;
        }

        logCollector.LogInfo(EnhancementLogEvent.Configuration,
            $"Using {expressions.Count} regex expressions",
            new { ExpressionCount = expressions.Count, Expressions = expressions });

        var ctx = new RegexEnhancerContext();

        foreach (var exp in expressions)
        {
            var regex = new System.Text.RegularExpressions.Regex(exp, RegexOptions.IgnoreCase);
            var mergedNamedGroups = regex.MatchAllAndMergeByNamedGroups(resource.FileName);
            if (mergedNamedGroups.Any())
            {
                logCollector.LogInfo(EnhancementLogEvent.RegexMatched,
                    $"Pattern matched with {mergedNamedGroups.Count} groups",
                    new { Pattern = exp, MatchedGroups = mergedNamedGroups.Keys.ToList() });
            }

            foreach (var (gn, values) in mergedNamedGroups)
            {
                ctx.CaptureGroupsAndValues ??= [];
                if (!ctx.CaptureGroupsAndValues.TryGetValue(gn, out var list))
                {
                    ctx.CaptureGroupsAndValues[gn] = list = [];
                }

                list.AddRange(values);
            }
        }

        if (ctx.CaptureGroupsAndValues?.Any() == true)
        {
            logCollector.LogInfo(EnhancementLogEvent.ContextBuilt,
                $"Captured {ctx.CaptureGroupsAndValues.Count} groups",
                new { Groups = ctx.CaptureGroupsAndValues });
        }

        return ctx;
    }


    protected override EnhancerId TypedId => EnhancerId.Regex;

    protected override async Task<List<EnhancementTargetValue<RegexEnhancerTarget>>> ConvertContextByTargets(
        RegexEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<RegexEnhancerTarget>>();

        foreach (var target in SpecificEnumUtils<RegexEnhancerTarget>.Values)
        {
            switch (target)
            {
                case RegexEnhancerTarget.CaptureGroups:
                {
                    if (context.CaptureGroupsAndValues != null)
                    {
                        foreach (var (key, values) in context.CaptureGroupsAndValues)
                        {
                            enhancements.Add(new EnhancementTargetValue<RegexEnhancerTarget>(
                                RegexEnhancerTarget.CaptureGroups, key,
                                new ListStringValueBuilder(values)));
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return enhancements;
    }
}