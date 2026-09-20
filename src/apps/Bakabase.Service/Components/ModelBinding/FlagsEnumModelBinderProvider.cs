using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.Service.Components.ModelBinding;

/// <summary>
/// Hands every <see cref="FlagsAttribute"/> enum to <see cref="FlagsEnumModelBinder"/>.
/// See that type for why the built-in enum binder is not good enough.
/// </summary>
public class FlagsEnumModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // An explicitly requested binder, or a value that comes from the body or from DI,
        // is none of our business. This matters only when Register() could not find the
        // built-in enum provider and fell back to the head of the list.
        if (context.BindingInfo.BinderType != null ||
            context.BindingInfo.BindingSource == BindingSource.Body ||
            context.BindingInfo.BindingSource == BindingSource.Services ||
            context.BindingInfo.BindingSource == BindingSource.Special)
        {
            return null;
        }

        var modelType = context.Metadata.UnderlyingOrModelType;

        return modelType.IsEnum && modelType.IsDefined(typeof(FlagsAttribute), false)
            ? new FlagsEnumModelBinder(modelType)
            : null;
    }

    /// <summary>
    /// Inserts the provider immediately ahead of MVC's enum provider — the one it is
    /// meant to replace — so everything ordered before it (explicit binders, body, DI)
    /// keeps winning.
    /// </summary>
    public static void Register(IList<IModelBinderProvider> providers)
    {
        var index = 0;
        for (var i = 0; i < providers.Count; i++)
        {
            if (providers[i].GetType().Name == "EnumTypeModelBinderProvider")
            {
                index = i;
                break;
            }
        }

        providers.Insert(index, new FlagsEnumModelBinderProvider());
    }
}
