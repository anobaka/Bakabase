using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Models.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class ResourceProfileSourceReaderTests
{
    [TestMethod]
    public async Task SearchMetadataContainsOnlySearchVocabularyNotDefinitionDefaults()
    {
        var properties = DispatchProxy.Create<IPropertyService, Properties>();
        ((Properties) properties).Items = [new(PropertyPool.Custom, 17, PropertyType.SingleChoice, "Genre",
            new SingleChoicePropertyOptions {IgnoreCase = true, DefaultValue = "private-default",
                Choices = [new ChoiceOptions {Value = "movie", Label = "Movie"}]})];
        var reader = new ResourceProfileSourceReader(properties, DispatchProxy.Create<IPropertyLocalizer, Localizer>(),
            null!, null!, null!);
        var response = await reader.PropertiesAsync(default);
        var property = response.Data.Single();
        Assert.AreEqual(17, property.Id);
        Assert.AreEqual("Genre", property.Name);
        var options = (JObject) property.Options!;
        Assert.IsTrue(options.Value<bool>("ignoreCase"));
        Assert.AreEqual("movie", options["choices"]![0]!["value"]!.Value<string>());
        Assert.IsNull(options["defaultValue"]);
        Assert.IsFalse(options.ToString().Contains("private-default"));
    }

    [TestMethod]
    public async Task FilterValueDoesNotExposeDefinitionDefaultsEither()
    {
        var properties = DispatchProxy.Create<IPropertyService, Properties>();
        ((Properties) properties).Items = [new(PropertyPool.Custom, 17, PropertyType.SingleChoice, "Genre",
            new SingleChoicePropertyOptions {DefaultValue = "private-default", Choices = []})];
        var reader = new ResourceProfileSourceReader(properties, DispatchProxy.Create<IPropertyLocalizer, Localizer>(),
            null!, null!, null!);
        var response = await reader.FilterValueAsync(new ResourceSearchFilterInputModel
            {PropertyPool = PropertyPool.Custom, PropertyId = 17, Operation = SearchOperation.Equals}, default);
        Assert.IsFalse(JObject.FromObject(response).ToString().Contains("private-default"));
    }

    public class Properties : DispatchProxy
    {
        public List<Property> Items = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IPropertyService.GetProperties) => Task.FromResult(Items),
            nameof(IPropertyService.GetProperty) => Task.FromResult(Items.Single(p => p.Pool == (PropertyPool) args![0]! && p.Id == (int) args[1]!)),
            _ => throw new NotSupportedException(method.Name)
        };
    }

    public class Localizer : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => args?.FirstOrDefault()?.ToString() ?? "Property";
    }
}
