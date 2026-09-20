using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.Service.Components.ModelBinding;

/// <summary>
/// Binds a <see cref="FlagsAttribute"/> enum as what it is — a bit set — instead of
/// demanding that the number happens to be spellable with the declared members.
///
/// MVC's built-in enum binder accepts a flags value only when <c>Enum.ToString()</c>
/// resolves every bit to a name. That check misfires twice here:
///
/// * <c>ResourceAdditionalItem</c> declares composite members (<c>DisplayName</c> and
///   <c>Cover</c> both carry <c>Properties</c>). Formatting consumes <c>Cover</c> first,
///   which eats the shared <c>Properties</c> bit, and <c>DisplayName</c>'s own bit is then
///   left over — so a request for display name *and* cover is answered with
///   "The value '16672' is invalid."
/// * Widening an <c>All</c> member changes its number, so every client built before that
///   change sends a value this server can no longer format. That is how
///   <c>additionalItems=52064</c> — the <c>All</c> of builds without
///   <c>CollectionName</c> — turned into a 400 on startup for anyone still running an
///   older client or an older cached web bundle.
///
/// Unknown bits are dropped rather than rejected: a newer client asking for something
/// this build has never heard of should get the rest of what it asked for.
/// </summary>
public class FlagsEnumModelBinder : IModelBinder
{
    private readonly Type _enumType;
    private readonly ulong _definedBits;

    public FlagsEnumModelBinder(Type enumType)
    {
        _enumType = enumType;
        _definedBits = Enum.GetValues(enumType).Cast<object>().Aggregate(0UL, (bits, v) => bits | ToUInt64(v));
    }

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Same as the built-in binder: an empty value is only meaningful for a
            // nullable parameter; otherwise leave the parameter at its default.
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }

            return Task.CompletedTask;
        }

        // Accepts both numbers and comma-separated member names, exactly like the
        // built-in binder does before its formatting check.
        if (!Enum.TryParse(_enumType, value, true, out var parsed) || parsed == null)
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                bindingContext.ModelMetadata.ModelBindingMessageProvider.ValueIsInvalidAccessor(
                    valueProviderResult.ToString()));
            return Task.CompletedTask;
        }

        var masked = ToUInt64(parsed) & _definedBits;
        bindingContext.Result = ModelBindingResult.Success(Enum.ToObject(_enumType, masked));

        return Task.CompletedTask;
    }

    private static ulong ToUInt64(object enumValue) =>
        Convert.GetTypeCode(enumValue) switch
        {
            TypeCode.SByte => unchecked((ulong) Convert.ToSByte(enumValue)),
            TypeCode.Int16 => unchecked((ulong) Convert.ToInt16(enumValue)),
            TypeCode.Int32 => unchecked((ulong) Convert.ToInt32(enumValue)),
            TypeCode.Int64 => unchecked((ulong) Convert.ToInt64(enumValue)),
            _ => Convert.ToUInt64(enumValue)
        };
}
