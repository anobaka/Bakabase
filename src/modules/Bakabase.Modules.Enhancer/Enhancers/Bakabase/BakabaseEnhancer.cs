using System.Globalization;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;
using Bootstrap.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Enhancers.Bakabase
{
    public class BakabaseEnhancer(
        IEnumerable<IStandardValueHandler> valueConverters,
        ILoggerFactory loggerFactory,
        ISpecialTextService specialTextService,
        IBakabaseLocalizer localizer)
        : AbstractEnhancer<BakabaseEnhancerTarget, BakabaseEnhancerContext, object?>(valueConverters, loggerFactory)
    {
        protected override EnhancerId TypedId => EnhancerId.Bakabase;
        private readonly IBakabaseLocalizer _localizer = localizer;

        protected override async Task<BakabaseEnhancerContext> BuildContext(Resource resource)
        {
            var name = resource.FileName;
            if (name.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(name));
            }

            var enhancements = new List<Enhancement>();
            var ctx = new BakabaseEnhancerContext();

            name = await specialTextService.Pretreatment(name);

            if (IsValid(name))
            {
                // ReleaseDt and Language can be matched simply
                var allWcs = MatchAllContentsWithWrappers(name);

                var nameRef = new RefWrapper<string>(name);
                var releaseDt = await GetAndRemoveReleaseDt(nameRef, allWcs);
                name = nameRef.Value;

                var (language, restName) = await GetAndRemoveLanguageWithWrapper(name, allWcs);
                name = restName;

                var publishers = GetAndRemovePublishers(ref name);
                var originals = GetAndRemoveOriginals(ref name);
                var volume = await TryToParseVolume(name);
                if (volume != null)
                {
                    name = name[..volume.Value.Index];
                }

                if (releaseDt.HasValue)
                {
                    ctx.ReleaseDt = releaseDt.Value;
                }

                if (!string.IsNullOrEmpty(language))
                {
                    ctx.Language = language;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    ctx.Name = name;
                }

                if (originals?.Any() == true)
                {
                    ctx.Originals = originals;
                }

                if (publishers?.Any() == true)
                {
                    ctx.Publishers = publishers;
                }

                if (volume != null)
                {
                    ctx.VolumeName = volume.Value.VolumeName;
                    ctx.VolumeTitle = volume.Value.VolumeTitle;
                }
            }

            ctx.Name = name;

            return ctx;
        }

        protected override async Task<Dictionary<BakabaseEnhancerTarget, IStandardValueBuilder>>
            ConvertContextByTargets(
                BakabaseEnhancerContext context)
        {
            var dict = new Dictionary<BakabaseEnhancerTarget, IStandardValueBuilder>();
            foreach (var target in SpecificEnumUtils<BakabaseEnhancerTarget>.Values)
            {
                IStandardValueBuilder valueBuilder = target switch
                {
                    BakabaseEnhancerTarget.Name => new StringValueBuilder(context.Name),
                    BakabaseEnhancerTarget.Publisher => new ListStringValueBuilder(context.Publishers),
                    BakabaseEnhancerTarget.ReleaseDt => new DateTimeValueBuilder(context.ReleaseDt),
                    BakabaseEnhancerTarget.VolumeName => new StringValueBuilder(context.VolumeName),
                    BakabaseEnhancerTarget.VolumeTitle => new StringValueBuilder(context.VolumeTitle),
                    BakabaseEnhancerTarget.Originals => new ListStringValueBuilder(context.Originals),
                    BakabaseEnhancerTarget.Language => new StringValueBuilder(context.Language),
                    _ => throw new ArgumentOutOfRangeException()
                };

                if (valueBuilder.Value != null)
                {
                    dict[target] = valueBuilder;
                }
            }

            return dict;
        }

        private async Task<DateTime?> GetAndRemoveReleaseDt(RefWrapper<string> name,
            IEnumerable<WrappedContent> wcs)
        {
            foreach (var wc in wcs)
            {
                var dt = await specialTextService.TryToParseDateTime(wc.Content);
                if (dt.HasValue)
                {
                    name.Value = $"{name.Value[..wc.Index]}{name.Value[(wc.Index + wc.ContentWithWrapper.Length)..]}";
                    return dt.Value;
                }
            }

            return null;
        }



        private async Task<(string? Language, string RestName)> GetAndRemoveLanguageWithWrapper(string name, IEnumerable<WrappedContent> wcs)
        {
            var languageWords =
                (await specialTextService.GetAll(SpecialTextType.Language, false)).ToDictionary(t => t.Value1,
                    t => t.Value2!);

            foreach (var wc in wcs)
            {
                foreach (var (reg, languageValue) in languageWords)
                {
                    if (Regex.IsMatch(wc.Content, reg))
                    {
                        name = $"{name[..wc.Index]}{name[(wc.Index + wc.ContentWithWrapper.Length)..]}";
                        return (languageValue, name);
                    }
                }
            }

            return (null, name);
        }

        private static List<string>? GetAndRemovePublishers(ref string name)
        {
            if (name.StartsWith('['))
            {
                var endIndex = name.IndexOf(']');
                if (endIndex > 0)
                {
                    var publisherString = name.Substring(1, endIndex - 1);
                    name = name[(endIndex + 1)..];
                    return publisherString.AnalyzePublishers().Extract().Select(p => p.Name).Distinct().ToList();
                }
            }

            return null;
        }

        private static List<string>? GetAndRemoveOriginals(ref string name)
        {
            if (name.EndsWith(')'))
            {
                var layer = 1;
                for (var i = name.Length - 2; i > -1; i--)
                {
                    var c = name[i];
                    if (c == ')')
                    {
                        layer++;
                    }

                    if (c == '(')
                    {
                        layer--;
                    }

                    if (layer == 0)
                    {
                        var originals = name.Substring(i + 1, name.Length - i - 2);
                        name = name.Substring(0, i);
                        return originals.Split(new[] {'、'}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()).Distinct().ToList();
                    }
                }
            }

            return null;
        }

        private bool IsValid(string name)
        {
            var wrappers = specialTextService[SpecialTextType.Wrapper];
            foreach (var wrapper in wrappers)
            {
                var left = wrapper.Value1;
                var right = wrapper.Value2!;
                var layer = 0;
                for (var i = 0; i < name.Length; i++)
                {
                    if (i + left.Length <= name.Length && name.Substring(i, left.Length) == left)
                    {
                        layer++;
                    }

                    if (i + right.Length <= name.Length && name.Substring(i, right.Length) == right)
                    {
                        layer--;
                    }

                    if (layer < 0)
                    {
                        return false;
                    }
                }

                if (layer != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<(string VolumeName, string VolumeTitle, string Match, int Index)?> TryToParseVolume(
            string str)
        {
            var volumeTexts = await specialTextService.GetAll(SpecialTextType.Volume, false);
            foreach (var v in volumeTexts)
            {
                var reg = new Regex($"(?<volumeIndexName>{v.Value1})(?<volumeTitle>.*)$");
                var match = reg.Match(str);
                if (match.Success)
                {
                    var volumeIndexName = match.Groups["volumeIndexName"].Value.Trim();
                    return (volumeIndexName, match.Groups["volumeTitle"].Value.Trim(), match.Value, match.Index);
                }
            }

            return null;
        }

        private record WrappedContent
        {
            public string Content { get; init; } = null!;
            public string ContentWithWrapper { get; init; } = null!;
            public int Index { get; init; }
        }

        private WrappedContent[] MatchAllContentsWithWrappers(string str)
        {
            var wrappers = specialTextService[SpecialTextType.Wrapper];

            var matches = new List<(string Left, string Right, Match Match, string Content)>();
            foreach (var w in wrappers)
            {
                var r = StringHelpers.BuildRegexWithWrapper(w.Value1, w.Value2!, ".+?");
                var m = r.Match(str);
                var idx = 0;
                while (m.Success)
                {
                    if (m.Index >= idx)
                    {
                        var content =
                            m.Value[(w.Value1?.Length ?? 0)..][
                                ..((m.Value.Length - w.Value1?.Length ?? 0 - w.Value2?.Length ?? 0) - 1)];
                        matches.Add((w.Value1!, w.Value2!, m, content));
                        idx = m.Index + m.Length;
                    }

                    m = m.NextMatch();
                }
            }

            var orderedMatches = matches.OrderBy(a => a.Match.Index).ToArray();
            var wcs = orderedMatches.Select(om => new WrappedContent
            {
                Content = om.Content,
                ContentWithWrapper = om.Match.Value,
                Index = om.Match.Index
            }).ToArray();
            return wcs;
        }
    }
}