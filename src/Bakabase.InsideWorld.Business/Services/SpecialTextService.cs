using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
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
    public class SpecialTextService : FullMemoryCacheResourceService<InsideWorldDbContext, SpecialText, int>,
        ISpecialTextService
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

        public async Task<DateTime?> TryToParseDateTime(string? str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return null;
            }

            var r = await TryToParseDateTime([str]);
            return r?.Any() == true ? r[0].DateTime : null;
        }

        public async Task<List<(int Index, DateTime DateTime)>> TryToParseDateTime(string[] strings)
        {
            var list = new List<(int Index, DateTime DateTime)>();
            if (strings.Any())
            {
                var texts = await GetAll(t => t.Type == SpecialTextType.DateTime);
                var formats = texts.Select(a => a.Value1).Distinct().ToArray();
                for (var i = 0; i < strings.Length; i++)
                {
                    if (DateTime.TryParseExact(strings[i], formats, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeLocal,
                            out var dt))
                    {
                        list.Add((i, dt));
                    }
                    else
                    {
                        // fallback
                        if (DateTime.TryParse(strings[i], out var fallbackDt))
                        {
                            list.Add((i, fallbackDt));
                        }
                    }
                }

                return list;
            }

            return list;
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
            trimFlag.AddRange(wrappers.Select(a => a.Value2!));
            name = trimFlag.Aggregate(name,
                (current, replace) => Regex.Replace(current, $@"\s*{Regex.Escape(replace)}\s*", replace));

            // Useless words
            var uselessWords = this[SpecialTextType.Useless];
            name = uselessWords.Aggregate(name, (current1, word) => wrappers.Aggregate(current1, (current, wrapper) =>
            {
                // Must be wrapped.
                var reg = StringHelpers.BuildRegexWithWrapper(wrapper.Value1, wrapper.Value2!, word.Value1);
                current = reg.Replace(current, string.Empty);
                return current;
            }));

            return name;
        }
    }
}