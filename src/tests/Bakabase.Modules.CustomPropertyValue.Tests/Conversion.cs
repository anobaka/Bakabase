using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components.Properties.Attachment;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Tests.Components;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Extensions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.CustomPropertyValue.Tests
{
    [TestClass]
    public class Conversion
    {
        private static async Task<IServiceProvider> PrepareScopedServiceProvider()
        {
            var dbFilePath = Path.Combine(Directory.GetCurrentDirectory(), "test.db");
            File.Delete(dbFilePath);
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddLocalization();

            sc.AddSingleton<NoneCustomDateTimeParser>();
            sc.AddStandardValue<NoneCustomDateTimeParser>();
            sc.AddReservedProperty();
            sc.AddBootstrapServices<InsideWorldDbContext>(c => c.UseBootstrapSqLite(Path.GetDirectoryName(dbFilePath), Path.GetFileNameWithoutExtension(dbFilePath)));
            sc.AddCustomProperty<CustomPropertyService, CustomPropertyValueService, CategoryCustomPropertyMappingService
                , InsideWorldLocalizer>();
            sc.AddTransient<InsideWorldLocalizer>();
            var sp = sc.BuildServiceProvider();
            var scope = sp.CreateAsyncScope();
            var scopeSp = scope.ServiceProvider;
            var ctx = scopeSp.GetRequiredService<InsideWorldDbContext>();
            await ctx.Database.MigrateAsync();

            return scopeSp;
        }

        [TestMethod]
        public async Task TestConversion()
        {
            var scopeSp = await PrepareScopedServiceProvider();

            var dataSets = StandardValueOptions.ExpectedConversions;

            var customPropertyService = scopeSp.GetRequiredService<ICustomPropertyService>();
            var customPropertyValueService = scopeSp.GetRequiredService<ICustomPropertyValueService>();
            var customPropertyDescriptors = scopeSp.GetRequiredService<ICustomPropertyDescriptors>();

            var nowStr = $"{DateTime.Now:yyyyMMddHHmmss}";
            var resourceIdOffset = 0;

            var customPropertyTypes = SpecificEnumUtils<CustomPropertyType>.Values.ToArray();

            foreach (var fromType in customPropertyTypes)
            {
                Console.WriteLine($@"Testing conversion from {fromType}...");

                var fromPropertiesAddModels = new List<CustomPropertyAddOrPutDto>();
                var fromPropertiesValues = new List<Abstractions.Models.Domain.CustomPropertyValue>();
                var fromCpd = customPropertyDescriptors[(int) fromType];

                foreach (var toType in customPropertyTypes)
                {
                    fromPropertiesAddModels.Add(new CustomPropertyAddOrPutDto()
                    {
                        Name = $"TestConversionFrom{fromType}To{toType}",
                        Type = (int) fromType
                    });
                }

                var customProperties = await customPropertyService.AddRange(fromPropertiesAddModels.ToArray());

                foreach (var cp in customProperties)
                {
                    await customPropertyService.EnableAddingNewDataDynamically(cp.Id);
                }

                customProperties = await customPropertyService.GetByKeys(customProperties.Select(c => c.Id));

                var cases =
                    new Dictionary<int, (CustomPropertyType ToType, Dictionary<int, (object? FromValue, object? ExpectedValue)> ResourceIdAndExpectedData
                        )>();

                for (var i = 0; i < customProperties.Count; i++)
                {
                    var toType = customPropertyTypes[i];
                    var cp = customProperties[i];
                    var data = dataSets[fromCpd.BizValueType][customPropertyDescriptors[(int)toType].BizValueType];
                    var @case = cases.GetOrAdd(cp.Id, () => (toType, []));

                    // if (fromType == CustomPropertyType.Multilevel && toType == CustomPropertyType.SingleLineText)
                    // {
                    //
                    // }

                    foreach (var (fromValue, expectedValue) in data)
                    {
                        var resourceId = customProperties[i].Id * 10000 + resourceIdOffset++;
                        fromPropertiesValues.Add(new Abstractions.Models.Domain.CustomPropertyValue
                        {
                            ResourceId = resourceId,
                            Value = fromCpd.PrepareDbValueFromBizValue(customProperties[i], fromValue).DbValue,
                            PropertyId = customProperties[i].Id,
                        });

                        @case.ResourceIdAndExpectedData.Add(resourceId, (fromValue, expectedValue));
                    }
                }

                var cpIds = customProperties.Select(c => c.Id).ToList();
                await customPropertyService.UpdateRange(customProperties.Select(cp => cp.ToDbModel()).ToArray());
                var updatedCustomProperties = await customPropertyService.GetByKeys(cpIds);

                await customPropertyValueService.AddRange(fromPropertiesValues);
                foreach (var cp in updatedCustomProperties)
                {
                    var @case = cases[cp.Id];

                    // if (fromType == CustomPropertyType.Tags && @case.ToType == CustomPropertyType.Number)
                    // {
                    //
                    // }

                    await customPropertyService.ChangeType(cp.Id, @case.ToType);
                    var actualPropertyValues = await customPropertyValueService.GetAll(c => c.PropertyId == cp.Id,
                        CustomPropertyValueAdditionalItem.BizValue, false);
                    foreach (var (resourceId, (fromValue, expectedValue)) in @case.ResourceIdAndExpectedData)
                    {
                        var actualPropertyValue = actualPropertyValues.FirstOrDefault(c => c.ResourceId == resourceId);
                        var act = actualPropertyValue?.BizValue?.SerializeAsStandardValue(
                            customPropertyDescriptors[(int) @case.ToType].BizValueType);
                        var expect =
                            expectedValue?.SerializeAsStandardValue(customPropertyDescriptors[(int) @case.ToType]
                                .BizValueType);
                        try
                        {
                            act.Should().Be(expect);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(
                                $@"Converting [{fromType}]{fromValue?.SerializeAsStandardValue(customPropertyDescriptors[(int) cp.Type].BizValueType)} to [{@case.ToType}], expected:{expect}, actual:{act}");
                            throw;
                        }
                    }
                }

                Console.WriteLine();
            }
        }
    }
}