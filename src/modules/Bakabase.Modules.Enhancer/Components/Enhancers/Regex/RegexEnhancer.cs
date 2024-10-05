using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
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
    IBOptions<EnhancerOptions> enhancerOptions)
    : AbstractEnhancer<RegexEnhancerTarget, RegexEnhancerContext, object?>(loggerFactory, fileManager)
{
    protected override async Task<RegexEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        var expressions = enhancerOptions.Value.RegexEnhancer?.Expressions ?? [];
        if (!expressions.Any())
        {
            return null;
        }

        var ctx = new RegexEnhancerContext();

        foreach (var exp in expressions)
        {
            var regex = new System.Text.RegularExpressions.Regex(exp, RegexOptions.IgnoreCase);
            var match = regex.Match(resource.FileName);
            if (match.Success)
            {
                foreach (var name in regex.GetGroupNames())
                {
                    if (!int.TryParse(name, out _))
                    {
                        ctx.CaptureGroupsAndValues ??= [];
                        if (!ctx.CaptureGroupsAndValues.TryGetValue(name, out var values))
                        {
                            ctx.CaptureGroupsAndValues[name] = values = new List<string>();
                        }

                        values.Add(match.Groups[name].Value);
                    }
                }
            }
        }

        return ctx;
    }


    protected override EnhancerId TypedId => EnhancerId.Regex;

    protected override async Task<List<EnhancementTargetValue<RegexEnhancerTarget>>> ConvertContextByTargets(
        RegexEnhancerContext context, CancellationToken ct)
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