using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Service.Components.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests;

/// <summary>
/// A user opening the app was told "additionalItems The value '52064' is invalid" — 52064
/// being the <c>ResourceAdditionalItem.All</c> of every build made before
/// <c>CollectionName</c> widened it. These lock the binder that stopped the API from
/// answering a perfectly meaningful bit set with a 400.
/// </summary>
[TestClass]
public class FlagsEnumModelBindingTests
{
    private const int AllBeforeCollectionName = 52064;

    [TestMethod]
    public void AllBeforeCollectionName_IsUnformattable()
    {
        // The premise of the bug: MVC's built-in enum binder rejects exactly the values
        // Enum.ToString() cannot spell out, and this is one of them. If this ever starts
        // formatting, the enum's shape changed and the rest of these tests are about
        // something else.
        Assert.AreEqual(AllBeforeCollectionName.ToString(CultureInfo.InvariantCulture),
            ((ResourceAdditionalItem) AllBeforeCollectionName).ToString());
    }

    [TestMethod]
    public async Task OldClientAll_IsAccepted()
    {
        var bound = await Bind<ResourceAdditionalItem>(AllBeforeCollectionName.ToString(CultureInfo.InvariantCulture));

        Assert.AreEqual((ResourceAdditionalItem) AllBeforeCollectionName, bound);
        // And still means what the old client meant by it.
        Assert.IsTrue(bound.HasFlag(ResourceAdditionalItem.DisplayName));
        Assert.IsTrue(bound.HasFlag(ResourceAdditionalItem.Cover));
        Assert.IsFalse(bound.HasFlag(ResourceAdditionalItem.CollectionName));
    }

    [TestMethod]
    public async Task CompositeMembersCombined_AreAccepted()
    {
        // DisplayName and Cover both carry Properties, which is what leaves an
        // unnameable bit behind when the two are asked for together.
        var value = (int) (ResourceAdditionalItem.DisplayName | ResourceAdditionalItem.Cover);

        Assert.AreEqual(ResourceAdditionalItem.DisplayName | ResourceAdditionalItem.Cover,
            await Bind<ResourceAdditionalItem>(value.ToString(CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public async Task CurrentAll_RoundTrips()
    {
        Assert.AreEqual(ResourceAdditionalItem.All,
            await Bind<ResourceAdditionalItem>(((int) ResourceAdditionalItem.All).ToString(CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public async Task MemberNames_StillBind()
    {
        Assert.AreEqual(ResourceAdditionalItem.Cover | ResourceAdditionalItem.Alias,
            await Bind<ResourceAdditionalItem>("Cover, Alias"));
    }

    [TestMethod]
    public async Task BitsThisBuildDoesNotKnow_AreDroppedRatherThanRejected()
    {
        // A newer client asking for something this build never heard of gets the rest of
        // what it asked for, not a 400.
        var value = (int) ResourceAdditionalItem.Cover | 1 << 30;

        Assert.AreEqual(ResourceAdditionalItem.Cover,
            await Bind<ResourceAdditionalItem>(value.ToString(CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public async Task Garbage_IsStillRejected()
    {
        var context = CreateContext(typeof(ResourceAdditionalItem), "not-a-value");

        await new FlagsEnumModelBinder(typeof(ResourceAdditionalItem)).BindModelAsync(context);

        Assert.IsFalse(context.Result.IsModelSet);
        Assert.AreEqual(1, context.ModelState.ErrorCount);
    }

    [TestMethod]
    public void Provider_SitsAheadOfTheBuiltInEnumProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddControllers();

        using var sp = services.BuildServiceProvider();
        var providers = sp.GetRequiredService<IOptions<MvcOptions>>().Value.ModelBinderProviders;

        var builtIn = providers.ToList().FindIndex(p => p.GetType().Name == "EnumTypeModelBinderProvider");

        Assert.IsTrue(builtIn >= 0,
            "MVC no longer ships EnumTypeModelBinderProvider, so Register() is now falling back to the head of the list.");

        FlagsEnumModelBinderProvider.Register(providers);

        Assert.AreEqual(builtIn, providers.ToList().FindIndex(p => p is FlagsEnumModelBinderProvider));
    }

    [TestMethod]
    public void Provider_IgnoresNonFlagsEnums()
    {
        Assert.IsNull(new FlagsEnumModelBinderProvider().GetBinder(
            new StubProviderContext(typeof(DayOfWeek))));
        Assert.IsNotNull(new FlagsEnumModelBinderProvider().GetBinder(
            new StubProviderContext(typeof(ResourceAdditionalItem))));
    }

    private static async Task<T> Bind<T>(string value) where T : struct, Enum
    {
        var context = CreateContext(typeof(T), value);

        await new FlagsEnumModelBinder(typeof(T)).BindModelAsync(context);

        // ErrorCount, not IsValid: SetModelValue leaves the entry Unvalidated, and an
        // unvalidated dictionary is never "valid" however few errors it holds.
        Assert.AreEqual(0, context.ModelState.ErrorCount, string.Join("; ",
            context.ModelState.SelectMany(kv => kv.Value!.Errors).Select(e => e.ErrorMessage)));
        Assert.IsTrue(context.Result.IsModelSet);

        return (T) context.Result.Model!;
    }

    private static DefaultModelBindingContext CreateContext(Type modelType, string value) =>
        new()
        {
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = "additionalItems",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new SingleValueProvider("additionalItems", value)
        };

    private sealed class SingleValueProvider(string key, string value) : IValueProvider
    {
        public bool ContainsPrefix(string prefix) => string.Equals(prefix, key, StringComparison.OrdinalIgnoreCase);

        public ValueProviderResult GetValue(string k) =>
            ContainsPrefix(k) ? new ValueProviderResult(value) : ValueProviderResult.None;
    }

    private sealed class StubProviderContext(Type modelType) : ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo { get; } = new();
        public override ModelMetadata Metadata { get; } = new EmptyModelMetadataProvider().GetMetadataForType(modelType);
        public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();
        public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotSupportedException();
    }
}
