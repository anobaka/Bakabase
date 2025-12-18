using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Tests.Components;
using Bootstrap.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.StandardValue.Tests
{
    [TestClass]
    public sealed class Conversion
    {
        [TestMethod]
        public void TestConversion()
        {
            Console.WriteLine($@"Prepared conversion cases:");
            Console.WriteLine();
            var dataSets = StandardValueSystem.GetExpectedConversions();
            Console.WriteLine();

            foreach (var fromType in SpecificEnumUtils<StandardValueType>.Values)
            {
                foreach (var toType in SpecificEnumUtils<StandardValueType>.Values)
                {
                    foreach (var data in dataSets[fromType][toType])
                    {
                        var actualToValue = StandardValueSystem.GetHandler(fromType).Convert(data.FromValue, toType);
                        try
                        {
                            JsonConvert.SerializeObject(actualToValue).Should()
                                .Be(JsonConvert.SerializeObject(data.ExpectedValue));
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(
                                $@"Testing {fromType} to {toType}, using data: {data.FromValue?.SerializeAsStandardValue(fromType) ?? "null"}, expected result: {data.ExpectedValue?.SerializeAsStandardValue(toType) ?? "null"}, actual: {actualToValue?.SerializeAsStandardValue(toType)}");
                            throw;
                        }
                    }
                }
            }
        }
    }
}