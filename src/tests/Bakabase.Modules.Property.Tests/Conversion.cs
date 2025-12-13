using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Infrastructures.Components.Orm;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.ReservedProperty;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Tests.Components;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Extensions;
using DotNext.Collections.Generic;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Tests
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
            sc.AddBootstrapServices<BakabaseDbContext>(c => c.UseBootstrapSqLite(Path.GetDirectoryName(dbFilePath), Path.GetFileNameWithoutExtension(dbFilePath)));
            sc.AddProperty<BakabaseDbContext>();
            sc.AddTransient<BakabaseLocalizer>();
            var sp = sc.BuildServiceProvider();
            var scope = sp.CreateAsyncScope();
            var scopeSp = scope.ServiceProvider;
            var ctx = scopeSp.GetRequiredService<BakabaseDbContext>();
            await ctx.Database.MigrateAsync();

            return scopeSp;
        }

        [TestMethod]
        public async Task TestConversion()
        {
            var scopeSp = await PrepareScopedServiceProvider();

            var dataSets = StandardValueSystem.GetExpectedConversions();

            var customPropertyService = scopeSp.GetRequiredService<ICustomPropertyService>();
            var customPropertyValueService = scopeSp.GetRequiredService<ICustomPropertyValueService>();

            var nowStr = $"{DateTime.Now:yyyyMMddHHmmss}";
            var resourceIdOffset = 0;

            var customPropertyTypes = SpecificEnumUtils<PropertyType>.Values.ToArray();

            foreach (var fromType in customPropertyTypes)
            {
                Console.WriteLine($@"Testing conversion from {fromType}...");

                var fromPropertiesAddModels = new List<CustomPropertyAddOrPutDto>();
                var fromPropertiesValues = new List<Bakabase.Abstractions.Models.Domain.CustomPropertyValue>();
                var fromCpd = PropertySystem.Property.GetDescriptor(fromType);

                foreach (var toType in customPropertyTypes)
                {
                    fromPropertiesAddModels.Add(new CustomPropertyAddOrPutDto()
                    {
                        Name = $"TestConversionFrom{fromType}To{toType}",
                        Type = fromType
                    });
                }

                var customProperties = await customPropertyService.AddRange(fromPropertiesAddModels.ToArray());

                // foreach (var cp in customProperties)
                // {
                //     await customPropertyService.EnableAddingNewDataDynamically(cp.Id);
                // }

                customProperties = await customPropertyService.GetByKeys(customProperties.Select(c => c.Id));

                var cases =
                    new Dictionary<int, (PropertyType ToType, Dictionary<int, (object? FromValue, object? ExpectedValue)> ResourceIdAndExpectedData
                        )>();

                var properties = customProperties.Select(cp => cp.ToProperty()).ToList();

                for (var i = 0; i < properties.Count; i++)
                {
                    var toType = customPropertyTypes[i];
                    var cp = customProperties[i];
                    var property = properties[i];
                    var data = dataSets[fromCpd.BizValueType][PropertySystem.Property.GetBizValueType(toType)];
                    var @case = cases.GetOrAdd(cp.Id, k => (toType, []));

                    foreach (var (fromValue, expectedValue) in data)
                    {
                        var resourceId = property.Id * 10000 + resourceIdOffset++;
                        fromPropertiesValues.Add(new Bakabase.Abstractions.Models.Domain.CustomPropertyValue
                        {
                            ResourceId = resourceId,
                            Value = fromCpd.PrepareDbValue(property, fromValue).DbValue,
                            PropertyId = property.Id,
                        });

                        @case.ResourceIdAndExpectedData.Add(resourceId, (fromValue, expectedValue));
                    }
                }

                var cpIds = customProperties.Select(c => c.Id).ToList();
                await customPropertyService.UpdateRange(properties.Select(cp => cp.ToCustomProperty().ToDbModel())
                    .ToArray());
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
                            PropertySystem.Property.GetBizValueType(@case.ToType));
                        var expect =
                            expectedValue?.SerializeAsStandardValue(PropertySystem.Property.GetBizValueType(@case.ToType));
                        try
                        {
                            act.Should().Be(expect);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(
                                $@"Converting [{fromType}]{fromValue?.SerializeAsStandardValue(PropertySystem.Property.GetBizValueType(cp.Type))} to [{@case.ToType}], expected:{expect}, actual:{act}");
                            throw;
                        }
                    }
                }

                Console.WriteLine();
            }
        }
    }
}