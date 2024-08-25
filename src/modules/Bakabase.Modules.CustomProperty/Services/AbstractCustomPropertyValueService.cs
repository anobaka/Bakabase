using System.Linq.Expressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Helpers;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.CustomProperty.Services
{
    public abstract class
        AbstractCustomPropertyValueService<TDbContext>(
            IServiceProvider serviceProvider,
            IEnumerable<IStandardValueHandler> converters,
            ICustomPropertyDescriptors propertyDescriptors,
            ICustomPropertyLocalizer localizer,
            IBuiltinPropertyValueService _builtinPropertyValueService,
            IStandardValueLocalizer standardValueLocalizer)
        : FullMemoryCacheResourceService<TDbContext, Bakabase.Abstractions.Models.Db.CustomPropertyValue, int>(
            serviceProvider), ICustomPropertyValueService where TDbContext : DbContext
    {
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();

        private readonly Dictionary<StandardValueType, IStandardValueHandler> _converters =
            converters.ToDictionary(d => d.Type, d => d);

        public async Task<List<CustomPropertyValue>> GetAll(Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, bool>>? exp,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var data = await GetAll(exp, returnCopy);
            return await ToDomainModels(data, additionalItems, returnCopy);
        }

        public Task<List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>> GetAllDbModels(
            Expression<Func<Bakabase.Abstractions.Models.Db.CustomPropertyValue, bool>>? selector = null,
            bool returnCopy = true) =>
            base.GetAll(selector, returnCopy);

        protected async Task<List<Bakabase.Abstractions.Models.Domain.CustomPropertyValue>> ToDomainModels(
            List<Bakabase.Abstractions.Models.Db.CustomPropertyValue> values,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var propertyIds = values.Select(v => v.PropertyId).ToHashSet();
            var properties =
                await CustomPropertyService.GetAll(x => propertyIds.Contains(x.Id),
                    CustomPropertyAdditionalItem.None, returnCopy);
            var propertyMap = properties.ToDictionary(x => x.Id);
            var dtoList = values.Select(v => propertyDescriptors[propertyMap[v.PropertyId].Type].ToDomainModel(v)!)
                .ToList();

            foreach (var ai in SpecificEnumUtils<CustomPropertyValueAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(ai))
                {
                    switch (ai)
                    {
                        case CustomPropertyValueAdditionalItem.None:
                            break;
                        case CustomPropertyValueAdditionalItem.BizValue:
                        {
                                foreach (var dto in dtoList)
                                {
                                    if (propertyMap.TryGetValue(dto.PropertyId, out var p))
                                    {
                                        if (propertyDescriptors.TryGet(p.Type, out var cpd))
                                        {
                                            dto.BizValue = cpd.ConvertDbValueToBizValue(p, dto.Value);
                                        }
                                    }
                                }
                                break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            foreach (var dto in dtoList)
            {
                dto.Property = propertyMap[dto.PropertyId];
            }

            return dtoList;
        }

        public async Task<BaseResponse> AddRange(IEnumerable<CustomPropertyValue> values)
        {
            var customPropertyValues = values as CustomPropertyValue[] ?? values.ToArray();
            var pIds = customPropertyValues.Select(v => v.PropertyId).ToHashSet();
            var propertyMap = (await CustomPropertyService.GetByKeys(pIds)).ToDictionary(d => d.Id, d => d);
            var dbModelsMap = customPropertyValues.ToDictionary(v => v.ToDbModel(propertyMap[v.PropertyId].DbValueType)!, v => v);
            await AddRange(dbModelsMap.Keys.ToList());
            foreach (var (k, v) in dbModelsMap)
            {
                v.Id = k.Id;
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateRange(IEnumerable<CustomPropertyValue> values)
        {
            var customPropertyValues = values as CustomPropertyValue[] ?? values.ToArray();
            var pIds = customPropertyValues.Select(v => v.PropertyId).ToHashSet();
            var propertyMap = (await CustomPropertyService.GetByKeys(pIds)).ToDictionary(d => d.Id, d => d);
            var dbModels = values.Select(v => v.ToDbModel(propertyMap[v.PropertyId].DbValueType)!).ToList();
            await UpdateRange(dbModels);
            return BaseResponseBuilder.Ok;
        }

        public async Task<SingletonResponse<Bakabase.Abstractions.Models.Db.CustomPropertyValue>> AddDbModel(Bakabase.Abstractions.Models.Db.CustomPropertyValue resource)
        {
            return await base.Add(resource);
        }

        public async Task<BaseResponse> UpdateDbModel(Bakabase.Abstractions.Models.Db.CustomPropertyValue resource)
        {
            return await base.Update(resource);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task SaveByResources(List<Resource> data)
        {
            var resourceProperties =
                data.ToDictionary(d => d.Id, d => d.Properties?.GetValueOrDefault((int)ResourcePropertyType.Custom))
                    .Where(d => d.Value != null).ToDictionary(d => d.Key, d => d.Value!);

            var propertyIds = resourceProperties.SelectMany(d => d.Value.Select(c => c.Key)).ToHashSet();
            var properties = await CustomPropertyService.GetByKeys(propertyIds, CustomPropertyAdditionalItem.None);
            var propertyMap = properties.ToDictionary(d => d.Id, d => d);

            // ResourceId - PropertyId - Scope - Value
            var resourceIds = resourceProperties.Keys.ToList();
            var dbValueMap =
                (await GetAll(x => resourceIds.Contains(x.ResourceId)))
                .GroupBy(d => d.ResourceId)
                .ToDictionary(d => d.Key,
                    d => d.GroupBy(x => x.PropertyId)
                        .ToDictionary(c => c.Key, c => c.ToDictionary(e => e.Scope, e => e)));
            var valuesToAdd = new List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>();
            var valuesToUpdate = new List<Bakabase.Abstractions.Models.Db.CustomPropertyValue>();
            var changedProperties = new HashSet<Bakabase.Abstractions.Models.Domain.CustomProperty>();

            foreach (var (resourceId, propertyValues) in resourceProperties)
            {
                foreach (var (propertyId, propertyValue) in propertyValues)
                {
                    var property = propertyMap.GetValueOrDefault(propertyId);
                    if (property != null)
                    {
                        if (!propertyDescriptors.TryGet(property.Type, out var pd))
                        {
                            Logger.LogError(localizer.CustomProperty_DescriptorNotFound(property.Type));
                            continue;
                        }

                        if (propertyValue.Values != null)
                        {
                            foreach (var v in propertyValue.Values)
                            {
                                var (rawDbValue, propertyChanged) = pd.PrepareDbValueFromBizValue(property, v.BizValue);

                                var dbPv = dbValueMap.GetValueOrDefault(resourceId)?.GetValueOrDefault(propertyId)
                                    ?.GetValueOrDefault(v.Scope);
                                if (dbPv == null)
                                {
                                    var pv = CustomPropertyValueHelper.CreateFromImplicitValue(rawDbValue,
                                        property.Type, resourceId, propertyId, v.Scope);
                                    valuesToAdd.Add(pv.ToDbModel(property.DbValueType)!);
                                }
                                else
                                {
                                    dbPv.Value = rawDbValue?.SerializeAsStandardValue(property.DbValueType);
                                    valuesToUpdate.Add(dbPv);
                                }

                                if (propertyChanged)
                                {
                                    changedProperties.Add(property);
                                }
                            }
                        }
                    }
                }
            }

            await CustomPropertyService.UpdateRange(changedProperties.Select(p => p.ToDbModel()!).ToList());
            await AddRange(valuesToAdd);
            await UpdateRange(valuesToUpdate);
        }

        public async Task<(Bakabase.Abstractions.Models.Domain.CustomPropertyValue Value, bool PropertyChanged)?> Create(object? bizValue, StandardValueType bizValueType, Bakabase.Abstractions.Models.Domain.CustomProperty property, int resourceId, int scope)
        {
            if (!propertyDescriptors.TryGet(property.Type, out var pd))
            {
                Logger.LogError(localizer.CustomProperty_DescriptorNotFound(property.Type));
                return null;
            }

            var stdValueConverter = _converters.GetValueOrDefault(bizValueType);
            if (stdValueConverter == null)
            {
                Logger.LogError(standardValueLocalizer.StandardValue_HandlerNotFound(bizValueType));
                return null;
            }

            var (dbInnerValue, propertyChanged) = pd.PrepareDbValueFromBizValue(property, bizValue);

            var pv = CustomPropertyValueHelper.CreateFromImplicitValue(dbInnerValue, property.Type, resourceId,
                property.Id, scope);

            return (pv, propertyChanged);

            // var propertyChanged = false;
            // switch ((CustomPropertyType) property.Type)
            // {
            //     case CustomPropertyType.SingleLineText:
            //     case CustomPropertyType.MultilineText:
            //         break;
            //     case CustomPropertyType.SingleChoice:
            //     {
            //         var typedProperty = property.Cast<SingleChoiceProperty>();
            //         var typedValue = bizValue as string;
            //         if (!string.IsNullOrEmpty(typedValue))
            //         {
            //             propertyChanged =
            //                 (typedProperty.Options ??= new ChoicePropertyOptions<string>())
            //                 .AddChoices(true, typedValue);
            //             nv = new StringValueBuilder(typedProperty.Options.Choices!.First(x => x.Label == typedValue)
            //                 .Value);
            //         }
            //
            //         break;
            //     }
            //     case CustomPropertyType.MultipleChoice:
            //     {
            //         var typedProperty = property.Cast<MultipleChoiceProperty>();
            //         var typedValue = bizValue as string[];
            //
            //         if (typedValue?.Any() == true)
            //         {
            //             propertyChanged = (typedProperty.Options ??= new ChoicePropertyOptions<List<string>>())
            //                 .AddChoices(true,
            //                     typedValue);
            //             nv = typedValue
            //                 .Select(v => typedProperty.Options.Choices!.First(x => x.Label == v).Value).ToArray();
            //         }
            //
            //         break;
            //     }
            //     case CustomPropertyType.Number:
            //     case CustomPropertyType.Percentage:
            //     case CustomPropertyType.Rating:
            //     case CustomPropertyType.Boolean:
            //     case CustomPropertyType.Link:
            //     case CustomPropertyType.Attachment:
            //     case CustomPropertyType.Date:
            //     case CustomPropertyType.DateTime:
            //     case CustomPropertyType.Time:
            //     case CustomPropertyType.Formula:
            //         break;
            //     case CustomPropertyType.Multilevel:
            //     {
            //         var typedProperty = property.Cast<MultilevelProperty>();
            //         var typedValue = nv as List<List<string>>;
            //         if (typedValue?.Any() == true)
            //         {
            //             var options = typedProperty.Options ??= new MultilevelPropertyOptions();
            //             propertyChanged = options.AddBranchOptions(typedValue);
            //             options.Data ??= [];
            //             MultilevelDataOptions? parent = null;
            //             foreach (var tv in typedValue)
            //             {
            //                 var children = parent == null ? options.Data : (parent.Children ??= []);
            //                 var child = children.FirstOrDefault(x => x.Label == tv) ??
            //                             new MultilevelDataOptions {Label = tv};
            //                 children.Add(child);
            //                 parent = child;
            //             }
            //         }
            //
            //         break;
            //     }
            //     default:
            //         throw new ArgumentOutOfRangeException();
            // }
            //
            //
            // var pv = CustomPropertyValueHelper.CreateFromImplicitValue(nv, (CustomPropertyType) property.Type, scope);
            // pv.PropertyId = property.Id;
            //
            // return (propertyChanged, pv);
        }
    }
}