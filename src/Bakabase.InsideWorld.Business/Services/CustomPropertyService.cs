using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Properties.Boolean;
using Bakabase.Modules.CustomProperty.Properties.Choice;
using Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.DateTime;
using Bakabase.Modules.CustomProperty.Properties.Formula;
using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using Bakabase.Modules.CustomProperty.Properties.Number;
using Bakabase.Modules.CustomProperty.Properties.Number.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;
using Bakabase.Modules.CustomProperty.Properties.Time;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services
{
    public class CustomPropertyService : FullMemoryCacheResourceService<InsideWorldDbContext, CustomProperty, int>,
        ICustomPropertyService
    {
        protected CategoryCustomPropertyMappingService CategoryCustomPropertyMappingService =>
            GetRequiredService<CategoryCustomPropertyMappingService>();

        protected CustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<CustomPropertyValueService>();

        protected ConversionService ConversionService => GetRequiredService<ConversionService>();
        protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();

        protected Dictionary<int, ICustomPropertyDescriptor> PropertyDescriptors =>
            GetRequiredService<IEnumerable<ICustomPropertyDescriptor>>().ToDictionary(d => d.Type, d => d);

        protected Dictionary<StandardValueType, IStandardValueHandler> StdValueHandlers =>
            GetRequiredService<IEnumerable<IStandardValueHandler>>().ToDictionary(d => d.Type, d => d);

        public CustomPropertyService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<List<Modules.CustomProperty.Models.CustomProperty>> GetAll(
            Expression<Func<CustomProperty, bool>>? selector = null,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await GetAll(selector, returnCopy);
            var dtoList = await ToDtoList(data, additionalItems);
            return dtoList;
        }

        public async Task<Modules.CustomProperty.Models.CustomProperty> GetByKey(int id,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await base.GetByKey(id, returnCopy);
            var dtoList = await ToDtoList([data], additionalItems);
            return dtoList.First();
        }

        public async Task<List<Modules.CustomProperty.Models.CustomProperty>> GetByKeys(IEnumerable<int> ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None,
            bool returnCopy = true)
        {
            var data = await base.GetByKeys(ids, returnCopy);
            var dtoList = await ToDtoList(data.ToList(), additionalItems);
            return dtoList;
        }

        public async Task<Dictionary<int, List<Modules.CustomProperty.Models.CustomProperty>>>
            GetByCategoryIds(int[] ids)
        {
            var mappings = await CategoryCustomPropertyMappingService.GetAll(x => ids.Contains(x.CategoryId));
            var propertyIds = mappings.Select(x => x.PropertyId).ToHashSet();
            var properties = await GetAll(x => propertyIds.Contains(x.Id));
            var propertyMap = properties.ToDictionary(x => x.Id);

            return mappings.GroupBy(x => x.CategoryId).ToDictionary(x => x.Key,
                x => x.Select(y => propertyMap.GetValueOrDefault(y.PropertyId)).Where(y => y != null).ToList())!;
        }

        private async Task<List<Modules.CustomProperty.Models.CustomProperty>> ToDtoList(
            List<CustomProperty> properties,
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

        public async Task<Modules.CustomProperty.Models.CustomProperty> Add(CustomPropertyAddOrPutDto model)
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

        public async Task<Modules.CustomProperty.Models.CustomProperty> Put(int id, CustomPropertyAddOrPutDto model)
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
            var propertyDescriptor = PropertyDescriptors[property.Type];
            var stdValueHandler = StdValueHandlers[property.ValueType];

            var typedValues = values.Select(v => propertyDescriptor.BuildValueForDisplay(property, v)).ToList();

            var lossMap = new Dictionary<StandardValueConversionLoss, List<string>>();
            var result = new CustomPropertyTypeConversionLossViewModel
            {
                TotalDataCount = typedValues.Count,
            };

            foreach (var d in typedValues)
            {
                var (nv, loss) =
                    await ConversionService.CheckConversionLoss(d, property.ValueType, type.ToStandardValueType());
                var list = SpecificEnumUtils<StandardValueConversionLoss>.Values.Where(s => loss?.HasFlag(s) == true)
                    .ToList();
                foreach (var l in list)
                {
                    var str = stdValueHandler.BuildDisplayValue(d);
                    if (!string.IsNullOrEmpty(str))
                    {
                        if (!lossMap.ContainsKey(l))
                        {
                            lossMap[l] = [];
                        }

                        lossMap[l].Add(str);
                    }
                }

                if (list.Any())
                {
                    result.IncompatibleDataCount++;
                }
            }

            result.LossData = lossMap.Any() ? lossMap.ToDictionary(x => (int) x.Key, x => x.Value.ToArray()) : null;

            return result;
        }

        public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        {
            var property = await GetByKey(id, CustomPropertyAdditionalItem.None);
            object newOptions;
            switch (property.EnumType)
            {
                case CustomPropertyType.SingleChoice:
                {
                    var options = (property as SingleChoiceProperty)?.Options ?? new ChoicePropertyOptions<string>();
                    options.AllowAddingNewDataDynamically = true;
                    newOptions = options;
                    break;
                }
                case CustomPropertyType.MultipleChoice:
                {
                    var options = (property as MultipleChoiceProperty)?.Options ??
                                  new ChoicePropertyOptions<List<string>>();
                    options.AllowAddingNewDataDynamically = true;
                    newOptions = options;
                    break;
                }
                case CustomPropertyType.Multilevel:
                {
                    var options = (property as MultilevelProperty)?.Options ?? new MultilevelPropertyOptions();
                    options.AllowAddingNewDataDynamically = true;
                    newOptions = options;
                    break;
                }
                default:
                    return BaseResponseBuilder.BuildBadRequest($"Bad type for current property: {property.EnumType}");
            }

            await Put(id, new CustomPropertyAddOrPutDto
            {
                Name = property.Name,
                Options = JsonConvert.SerializeObject(newOptions),
                Type = property.Type
            });
            return BaseResponseBuilder.Ok;
        }
    }
}