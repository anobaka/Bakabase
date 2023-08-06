using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Prefabs;
using Bootstrap.Components.Cryptography;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Routing.Matching;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services
{
    public class SpecialTextService : FullMemoryCacheResourceService<InsideWorldDbContext, SpecialText, int>
    {
        public static string Version { get; private set; }

        public SpecialTextService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<SpecialText>> GetAll(SpecialTextType type, bool returnCopy = true)
        {
            return await base.GetAll(a => a.Type == type, returnCopy);
        }

        public async Task<string> CalcVersion()
        {
            var allTexts = await GetAll();
            var str = JsonConvert.SerializeObject(allTexts);
            return CryptographyUtils.Md5(str)[..6];
        }

        private async Task _onChange()
        {
            Version = await CalcVersion();
        }

        public async Task<SingletonResponse<SpecialText>> Add(SpecialTextCreateRequestModel model)
        {
            var t = new SpecialText
            {
                Type = model.Type,
                Value1 = model.Value1,
                Value2 = model.Value2
            };
            var r = await base.Add(t);
            await _onChange();
            return r;
        }

        public async Task<Dictionary<SpecialTextType, List<SpecialText>>> GetAll()
        {
            var raw = (await base.GetAll()).GroupBy(a => a.Type).ToDictionary(a => a.Key, a => a.ToList());
            return SpecificEnumUtils<SpecialTextType>.Values.ToDictionary(a => a,
                a => raw.TryGetValue(a, out var v) ? v : new List<SpecialText>());
        }

        public List<SpecialText> this[SpecialTextType type] => base.GetAll(t => t.Type == type).ConfigureAwait(false)
            .GetAwaiter().GetResult().ToList();

        public async Task<BaseResponse> UpdateByKey(int id, SpecialTextUpdateRequestModel model)
        {
            await base.UpdateByKey(id, t =>
            {
                if (!string.IsNullOrEmpty(model.Value1))
                {
                    t.Value1 = model.Value1;
                }

                if (!string.IsNullOrEmpty(model.Value2))
                {
                    t.Value2 = model.Value2;
                }
            });
            await _onChange();
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> AddPrefabs()
        {
            var texts = await GetAll();
            var newEntries =
                SpecialTextPrefabs.Texts.Where(a =>
                    !texts.TryGetValue(a.Type, out var exists) ||
                    exists.All(e => !(e.Value1 == a.Value1 && e.Value2 == a.Value2))).ToList();
            await AddRange(newEntries);
            await _onChange();
            return BaseResponseBuilder.Ok;
        }

        public async Task<string> Pretreatment(string name)
        {
            var prev = name;

            // Standardize
            var standardizers = this[SpecialTextType.Standardization];
            name = standardizers.Aggregate(name,
                (current, replace) => current.Replace(replace.Value1, replace.Value2));

            // Remove unnecessary spaces.
            name = new Regex(@"\s+").Replace(name, " ").Trim();

            var wrappers = this[SpecialTextType.Wrapper];

            // Trim
            var trimFlag = this[SpecialTextType.Trim].Select(t => t.Value1).ToList();
            // Add wrappers as trim targets.
            trimFlag.AddRange(wrappers.Select(a => a.Value1));
            trimFlag.AddRange(wrappers.Select(a => a.Value2));
            name = trimFlag.Aggregate(name,
                (current, replace) => Regex.Replace(current, $@"\s*{Regex.Escape(replace)}\s*", replace));

            // Useless words
            var uselessWords = this[SpecialTextType.Useless];
            name = uselessWords.Aggregate(name, (current1, word) => wrappers.Aggregate(current1, (current, wrapper) =>
            {
                // Must be wrapped.
                var reg = BuildRegexWithWrapper(wrapper.Value1, wrapper.Value2, word.Value1);
                current = reg.Replace(current, string.Empty);
                return current;
            }));

            return name;
        }

        public static Regex BuildRegexWithWrapper(string left, string right, string word)
        {
            //获取自动非left和right的padding
            //右中括号特殊处理
            var rBracket = right == "]" ? @"\]" : Regex.Escape(right);
            var lBracket = Regex.Escape(left);
            //如果左右为空，则不生成padding
            var leftPadding = true;
            if (word[0] == '^')
            {
                leftPadding = false;
                word = word.TrimStart('^');
            }

            var rightPadding = true;
            if (word.EndsWith("$") && !word.EndsWith(@"\$"))
            {
                rightPadding = false;
                word = word.TrimEnd('$');
            }

            var padding = string.IsNullOrEmpty(rBracket) && string.IsNullOrEmpty(lBracket)
                ? null
                : rBracket == lBracket
                    ? $"[^{rBracket}]*?"
                    : $"[^{lBracket}{rBracket}]*?";
            var lPadding = leftPadding ? padding : string.Empty;
            var rPadding = rightPadding ? padding : string.Empty;
            left = Regex.Escape(left);
            right = Regex.Escape(right);
            return new Regex($"{left}{lPadding}{word}{rPadding}{right}");
        }

        public async Task<DateTime?> TryToParseDateTime(string str)
        {
            var r = await TryToParseDateTime(new[] {str});
            return r.Any() ? r[0].DateTime : null;
        }

        public async Task<(int Index, DateTime DateTime)[]> TryToParseDateTime(string[] strings)
        {
            if (strings.Any())
            {
                var texts = await GetAll(SpecialTextType.DateTime, false);
                var formats = texts.Select(a => a.Value1).Distinct().ToArray();

                var list = new List<(int Index, DateTime DateTime)>();
                for (var i = 0; i < strings.Length; i++)
                {
                    if (DateTime.TryParseExact(strings[i], formats, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeLocal,
                            out var dt))
                    {
                        list.Add((i, dt));
                    }
                }

                return list.ToArray();
            }

            return Array.Empty<(int Index, DateTime DateTime)>();
        }

        public async Task<ResourceLanguage?> TryToParseLanguage(string str)
        {
            var texts = await GetAll(SpecialTextType.Language, false);
            foreach (var text in texts.Where(text => Regex.IsMatch(str, text.Value1, RegexOptions.IgnoreCase)))
            {
                return (ResourceLanguage) int.Parse(text.Value2);
            }

            return null;
        }

        public class WrappedContent
        {
            public string Content { get; set; }
            public string ContentWithWrapper { get; set; }
            public int Index { get; set; }
        }

        public async Task<WrappedContent[]> MatchAllContentsWithWrappers(string str)
        {
            var wrappers = await GetAll(SpecialTextType.Wrapper, false);

            var matches = new List<(string Left, string Right, Match Match, string Content)>();
            foreach (var w in wrappers)
            {
                var r = BuildRegexWithWrapper(w.Value1, w.Value2, ".+");
                var m = r.Match(str);
                while (m.Success)
                {
                    var content =
                        m.Value[(w.Value1?.Length ?? 0)..][
                            ..(m.Value.Length - w.Value1?.Length ?? 0 - w.Value2?.Length ?? 0)];
                    matches.Add((w.Value1, w.Value2, m, content));
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

        [ItemCanBeNull]
        public async Task<(VolumeDto Volume, string Match, int Index)?> TryToParseVolume(string str)
        {
            var volumeTexts = await GetAll(SpecialTextType.Volume, false);
            foreach (var v in volumeTexts)
            {
                var reg = new Regex($"(?<volumeIndexName>{v.Value1})(?<volumeTitle>.*)$");
                var match = reg.Match(str);
                if (match.Success)
                {
                    var volumeIndexName = match.Groups["volumeIndexName"].Value.Trim();
                    var indexStr = v.Value2 ?? Regex.Match(volumeIndexName, @"\d+").Value;
                    var m = Regex.Match(indexStr, "\\d+");
                    var index = m.Success ? int.Parse(m.Value) : 0;
                    var dto = new VolumeDto
                    {
                        Index = index,
                        Name = volumeIndexName,
                        Title = match.Groups["volumeTitle"].Value.Trim()
                    };
                    return (dto, match.Value, match.Index);
                }
            }

            return null;
        }
    }
}