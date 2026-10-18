using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Modules.Property.Components.Properties.Multilevel;
using Bakabase.Modules.Property.Components.Properties.Tags;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Service.Controllers;
using FluentAssertions;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class PropertyValueReadTests
{
    [TestMethod]
    [DataRow(PropertyType.SingleChoice)]
    [DataRow(PropertyType.MultipleChoice)]
    [DataRow(PropertyType.Tags)]
    [DataRow(PropertyType.Multilevel)]
    public async Task DbValueRead_MatchesExistingOptionsWithoutCreatingIdsOrChangingDefinitions(PropertyType type)
    {
        var existingId = Guid.NewGuid().ToString();
        object options = type switch
        {
            PropertyType.SingleChoice => new SingleChoicePropertyOptions
                {Choices = [new ChoiceOptions {Value = existingId, Label = "Existing"}]},
            PropertyType.MultipleChoice => new MultipleChoicePropertyOptions
                {Choices = [new ChoiceOptions {Value = existingId, Label = "Existing"}]},
            PropertyType.Tags => new TagsPropertyOptions
                {Tags = [new TagsPropertyOptions.TagOptions("Group", "Existing") {Value = existingId}]},
            _ => new MultilevelPropertyOptions
            {
                Data = [new MultilevelDataOptions {Value = "root-id", Label = "Root",
                    Children = [new MultilevelDataOptions {Value = existingId, Label = "Existing"}]}]
            }
        };
        var property = new Property(PropertyPool.Custom, 42, type, "Reference value", options);
        var controller = new PropertyController(new SharedPropertyService(property), null!, null!, null!, null!, null!, null!, null!);
        var originalOptions = JsonConvert.SerializeObject(property.Options);
        var expectedDbValue = type == PropertyType.SingleChoice ? (object)existingId : new List<string> {existingId};
        var expected = expectedDbValue.SerializeAsStandardValue(type.GetDbValueType());

        var matched = await controller.GetDbValue(property.Pool, property.Id, BusinessValue(type, "Existing"));
        matched.Data.Should().Be(expected);
        JsonConvert.SerializeObject(property.Options).Should().Be(originalOptions);

        var unmatched = await controller.GetDbValue(property.Pool, property.Id, BusinessValue(type, "Unknown"));
        unmatched.Data.Should().BeNull();
        JsonConvert.SerializeObject(property.Options).Should().Be(originalOptions);

        // A business label that looks exactly like an existing option ID is still a label.
        var idShapedLabel = await controller.GetDbValue(property.Pool, property.Id, BusinessValue(type, existingId));
        idShapedLabel.Data.Should().BeNull();
        JsonConvert.SerializeObject(property.Options).Should().Be(originalOptions);

        if (type != PropertyType.SingleChoice)
        {
            var partial = await controller.GetDbValue(property.Pool, property.Id,
                BusinessValue(type, "Existing", "Unknown"));
            partial.Data.Should().Be(expected);
            JsonConvert.SerializeObject(property.Options).Should().Be(originalOptions);
        }
    }

    private static string? BusinessValue(PropertyType type, params string[] labels)
    {
        object value = type switch
        {
            PropertyType.SingleChoice => labels[0],
            PropertyType.MultipleChoice => labels.ToList(),
            PropertyType.Tags => labels.Select(label => new TagValue("Group", label)).ToList(),
            _ => labels.Select(label => new List<string> {"Root", label}).ToList()
        };
        return value.SerializeAsStandardValue(type.GetBizValueType());
    }

    // Returning the same object also catches mutations to a shared/cached definition.
    private sealed class SharedPropertyService(Property property) : IPropertyService
    {
        public Task<Property> GetProperty(PropertyPool pool, int id) => Task.FromResult(property);
        public Task<List<Property>> GetProperties(PropertyPool pool, bool includeDeprecated = true) =>
            throw new InvalidOperationException("Unexpected catalog read");
    }
}
