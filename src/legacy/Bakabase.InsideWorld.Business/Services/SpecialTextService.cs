using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Prefabs;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class SpecialTextService(
        FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.SpecialText, int> orm)
        : ISpecialTextService, ICustomDateTimeParser
    {
        public async Task<List<SpecialText>> GetAll(
            Expression<Func<Abstractions.Models.Db.SpecialText, bool>>? selector = null, bool asNoTracking = true)
        {
            return (await orm.GetAll(selector, asNoTracking)).Select(s => s.ToDomainModel()).ToList();
        }

        public async Task<Dictionary<string, string>> GetAllWrappers()
        {
            var wrappers =
                (await GetAll(a => a.Type == SpecialTextType.Wrapper))
                .Where(a => a.Value1.IsNotEmpty() && a.Value2.IsNotEmpty())
                .GroupBy(a => a.Value1)
                .ToDictionary(a => a.Key, a => a.FirstOrDefault()!.Value2!);
            return wrappers;
        }

        public async Task<SingletonResponse<SpecialText>> Add(SpecialTextAddInputModel model)
        {
            var t = new SpecialText
            {
                Type = model.Type,
                Value1 = model.Value1,
                Value2 = model.Value2
            }.ToDbModel();
            var r = await orm.Add(t);
            return new SingletonResponse<SpecialText>(r.Data.ToDomainModel());
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

        public async Task<int> Count(Func<Abstractions.Models.Db.SpecialText, bool>? selector = null)
        {
            return await orm.Count(selector);
        }

        public async Task<BaseResponse> DeleteByKey(int key)
        {
            return await orm.RemoveByKey(key);
        }

        public async Task<BaseResponse> Patch(int id, SpecialTextPatchInputModel model)
        {
            await orm.UpdateByKey(id, t =>
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
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> AddPrefabs()
        {
            var texts = (await GetAll()).ToMap();
            var newEntries =
                SpecialTextPrefabs.Texts.Where(a =>
                    !texts.TryGetValue(a.Type, out var exists) ||
                    exists.All(e => !(e.Value1 == a.Value1 && e.Value2 == a.Value2))).ToList();
            await AddRange(newEntries);
            return BaseResponseBuilder.Ok;
        }

        public async Task<ListResponse<SpecialText>> AddRange(List<SpecialText> resources)
        {
            return new ListResponse<SpecialText>((await orm.AddRange(resources.Select(r => r.ToDbModel()).ToList()))
                .Data.Select(d => d.ToDomainModel()));
        }

        public async Task<string> Pretreatment(string name)
        {
            // Standardize
            var standardizers = await GetAll(x => x.Type == SpecialTextType.Standardization);
            name = standardizers.Aggregate(name,
                (current, replace) => current.Replace(replace.Value1, replace.Value2));

            // Remove unnecessary spaces.
            name = new Regex(@"\s+").Replace(name, " ").Trim();

            var wrappers = await GetAll(x => x.Type == SpecialTextType.Wrapper);

            // Trim
            var trimFlag = (await GetAll(x => x.Type == SpecialTextType.Trim)).Select(t => t.Value1).ToList();
            // Add wrappers as trim targets.
            trimFlag.AddRange(wrappers.Select(a => a.Value1));
            trimFlag.AddRange(wrappers.Select(a => a.Value2!));
            name = trimFlag.Aggregate(name,
                (current, replace) => Regex.Replace(current, $@"\s*{Regex.Escape(replace)}\s*", replace));

            // Useless words
            var uselessWords = await GetAll(x => x.Type == SpecialTextType.Useless);
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