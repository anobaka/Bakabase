using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Formula;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Time;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>
    {
        protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
            GetRequiredService<CategoryCustomPropertyMappingService>();

        protected CustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<CustomPropertyValueService>();

        protected ConversionService ConversionService => GetRequiredService<ConversionService>();

        protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();

        public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<Abstractions.Models.Domain.CustomProperty>> GetAll(
            Expression<Func<CustomProperty, bool>>? selector = null,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await GetAll(selector, returnCopy);
            var dtoList = await ToDtoList(data, additionalItems);
            return dtoList;
        }

        public async Task<Abstractions.Models.Domain.CustomProperty> GetByKey(int id,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await base.GetByKey(id, returnCopy);
            var dtoList = await ToDtoList([data], additionalItems);
            return dtoList.First();
        }

        public async Task<Dictionary<int, List<Abstractions.Models.Domain.CustomProperty>>> GetByCategoryIds(int[] ids)
        {
            var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
            var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
            var properties = await GetAll(x => propertyIds.Contains(x.Id));
            var propertyMap = properties.ToDictionary(x => x.Id);

            return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
                x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).Where(y => y != null).ToList())!;
        }

        private async Task<List<Abstractions.Models.Domain.CustomProperty>> ToDtoList(List<CustomProperty> properties,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            var dtoList = properties.Select(p => p.ToDomainModel()!).ToList();
            foreach (var ai in SpecificEnumUtils<CustomPropertyAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(ai))
                {
                    switch (ai)
                    {
                        case CustomPropertyAdditionalItem.None:
                            break;
                        case CustomPropertyAdditionalItem.Category:
                        {
                            var propertyIds = properties.Select(x => x.Id).ToHashSet();
                            var mappings =
                                (await CategoryCustomPropertyMappingService.GetAll(x =>
                                    propertyIds.Contains(x.PropertyId)))!;
                            var categoryIds = mappings.Select(x => x.CategoryId).ToHashSet();
                            var categories = await ResourceCategoryService.GetAllDto(x => categoryIds.Contains(x.Id));
                            var categoryMap = categories.ToDictionary(x => x.Id);
                            var propertyCategoryIdMap = mappings.GroupBy(x => x.PropertyId)
                                .ToDictionary(x => x.Key, x => x.Select(y => y.CategoryId).ToHashSet());

                            foreach (var dto in dtoList)
                            {
                                dto.Categories = propertyCategoryIdMap.GetValueOrDefault(dto.Id)
                                    ?.Select(x => categoryMap.GetValueOrDefault(x)).Where(x => x != null).ToList()!;
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return dtoList;
        }

        public async Task<Abstractions.Models.Domain.CustomProperty> Add(CustomPropertyAddOrPutDto model)
        {
            var data = await Add(new CustomProperty
            {
                CreatedAt = DateTime.Now,
                Name = model.Name,
                Options = model.Options,
                Type = model.Type
            });

            return data.Data.ToDomainModel()!;
        }

        public async Task<Abstractions.Models.Domain.CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
        {
            var rsp = await UpdateByKey(id, cp =>
            {
                cp.Name = model.Name;
                cp.Options = model.Options;
                cp.Type = model.Type;
            });

            return rsp.Data.ToDomainModel()!;
        }

        public override async Task<BaseResponse> RemoveByKey(int id)
        {
            await CategoryCustomPropertyMappingService.RemoveAll(x => x.PropertyId == id);
            await CustomPropertyValueService.RemoveAll(x => x.PropertyId == id);
            return await base.RemoveByKey(id);
        }

        public async Task<CustomPropertyTypeConversionLossViewModel> CalculateTypeConversionLoss(int id,
            CustomPropertyType type)
        {
            var property = await GetByKey(id);
            var values = await CustomPropertyValueService.GetAll(x => x.PropertyId == id,
                CustomPropertyValueAdditionalItem.None, false);

            List<object> typedValues;
            Func<object, string> getDisplayStr = s => s.ToString()!;
            switch (property.Type)
            {
                case CustomPropertyType.SingleLineText:
                case CustomPropertyType.MultilineText:
                {
                    typedValues =
                    [
                        ..values.Cast<TextPropertyValue>().Select(s => s.Value).Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.SingleChoice:
                {
                    var typedProperty = (property as SingleChoiceProperty)!;
                    var choiceMap = typedProperty.Options?.Choices?.ToDictionary(d => d.Id, d => d.Value);
                    typedValues =
                    [
                        ..values.Cast<SingleChoicePropertyValue>()
                            .Select(v => string.IsNullOrEmpty(v.Value) ? null : choiceMap?.GetValueOrDefault(v.Value))
                            .Where(s => !string.IsNullOrEmpty(s)).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.MultipleChoice:
                {
                    var typedProperty = (property as MultipleChoiceProperty)!;
                    var choiceMap = typedProperty.Options?.Choices?.ToDictionary(d => d.Id, d => d.Value);

                    var arrList = values.Cast<MultipleChoicePropertyValue>()
                        .Select(v =>
                            v.Value?.Any() == true
                                ? v.Value.Select(x => choiceMap?.GetValueOrDefault(x))
                                    .Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(a => a).ToList()
                                : null)
                        .Where(s => s?.Any() == true).ToList();

                    var uniqueArrList = new List<List<string>>();
                    foreach (var list in arrList)
                    {
                        if (uniqueArrList.All(x => x.Count != list!.Count || !x.Any(y => list.Contains(y))))
                        {
                            uniqueArrList.Add(list!);
                        }
                    }

                    typedValues = [..uniqueArrList];
                    getDisplayStr = s => string.Join(BusinessConstants.TextSeparator, (s as List<string>)!);
                    break;
                }
                case CustomPropertyType.Number:
                {
                    typedValues =
                    [
                        ..values.Cast<NumberPropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Percentage:
                {
                    typedValues =
                    [
                        ..values.Cast<PercentagePropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Rating:
                {
                    typedValues =
                    [
                        ..values.Cast<RatingPropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Boolean:
                {
                    typedValues =
                    [
                        ..values.Cast<BooleanPropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Link:
                {
                    typedValues =
                    [
                        ..values.Cast<LinkPropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Attachment:
                {
                    typedValues =
                    [
                        ..values.Cast<AttachmentPropertyValue>()
                            .Select(s => s.Value?.Where(v => !string.IsNullOrEmpty(v)).ToList())
                            .Where(s => s?.Any() == true).Distinct()
                    ];
                    getDisplayStr = s => string.Join(BusinessConstants.TextSeparator, (s as List<string>)!);
                    break;
                }
                case CustomPropertyType.Date:
                {
                    typedValues =
                    [
                        ..values.Cast<DatePropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    getDisplayStr = s => ((DateTime) s).ToString("yyyy-MM-dd");
                    break;
                }
                case CustomPropertyType.DateTime:
                {
                    typedValues =
                    [
                        ..values.Cast<DateTimePropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    getDisplayStr = s => ((DateTime) s).ToString("yyyy-MM-dd HH:mm:ss");
                    break;
                }
                case CustomPropertyType.Time:
                {
                    typedValues =
                    [
                        ..values.Cast<TimePropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    getDisplayStr = s => ((TimeSpan) s).ToString("g");
                    break;
                }
                case CustomPropertyType.Formula:
                {
                    typedValues =
                    [
                        ..values.Cast<FormulaPropertyValue>().Select(s => s.Value).Distinct()
                    ];
                    break;
                }
                case CustomPropertyType.Multilevel:
                {
                    var typedProperty = (property as MultilevelProperty)!;
                    var allChains = new List<List<List<string>>>();
                    foreach (var s in values.Cast<MultilevelPropertyValue>())
                    {
                        if (s.Value?.Any() == true)
                        {
                            var chains = s.Value
                                .Select(item =>
                                    typedProperty.Options?.Data?.Select(x => x.FindLabel(item))
                                        .FirstOrDefault(x => x != null)?.ToList()).OfType<List<string>>().ToList();

                            if (chains.Any())
                            {
                                allChains.Add(chains);
                            }
                        }
                    }

                    typedValues = [..allChains];
                    getDisplayStr = s => string.Join(BusinessConstants.TextSeparator, (s as string[])!);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var lossMap = new Dictionary<StandardValueConversionLoss, List<string>>();
            var result = new CustomPropertyTypeConversionLossViewModel
            {
                TotalDataCount = typedValues.Count,
            };

            foreach (var d in typedValues)
            {
                var (nv, loss) = await ConversionService.CheckConversionLoss(d, property.Type.ToStandardValueType(),
                    type.ToStandardValueType());
                var list = SpecificEnumUtils<StandardValueConversionLoss>.Values.Where(s => loss?.HasFlag(s) == true)
                    .ToList();
                foreach (var l in list)
                {
                    var str = getDisplayStr(d);
                    if (!lossMap.ContainsKey(l))
                    {
                        lossMap[l] = [];
                    }

                    lossMap[l].Add(str);
                }

                if (list.Any())
                {
                    result.IncompatibleDataCount++;
                }
            }

            result.LossData = lossMap.Any() ? lossMap.ToDictionary(x => (int) x.Key, x => x.Value.ToArray()) : null;

            return result;
        }
    }
}