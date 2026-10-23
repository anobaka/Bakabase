using System;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services;

internal static class PathMarkValueConfigValidator
{
    public static void Validate(PathMarkType type, string configJson)
    {
        if (type == PathMarkType.Property)
        {
            var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(configJson);
            if (config != null) Validate(config);
        }
        else if (type == PathMarkType.MediaLibrary)
        {
            var config = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(configJson);
            if (config != null) Validate(config);
        }
    }

    public static void Validate(PropertyMarkConfig config) => Validate(
        config.ValueType, config.ValueBasePath, config.ValueMatchMode, config.GetEffectiveValueMatchMode(),
        config.ValueLayer, config.ValueRegex, config.UsesResourceRelativeValueRegex());

    public static void Validate(MediaLibraryMarkConfig config)
    {
        if (config.ValueType == PropertyValueType.Fixed && config.MediaLibraryIds != null &&
            (config.MediaLibraryIds.Count == 0 || config.MediaLibraryIds.Any(id => id <= 0)))
            throw new ArgumentException("Select at least one positive media library ID.");
        Validate(config.ValueType, config.ValueBasePath, config.ValueMatchMode, config.GetEffectiveValueMatchMode(),
            config.LayerToMediaLibrary, config.RegexToMediaLibrary, false);
    }

    private static void Validate(PropertyValueType valueType, PathMarkValueBasePath basePath,
        PathMatchMode? explicitMode, PathMatchMode effectiveMode, int? layer, string? regex, bool legacyRegex)
    {
        if (!Enum.IsDefined(basePath)) throw new ArgumentException("Unknown dynamic value base path.");
        if (explicitMode.HasValue && !Enum.IsDefined(explicitMode.Value))
            throw new ArgumentException("Unknown dynamic value extraction mode.");
        if (valueType != PropertyValueType.Dynamic) return;

        // Old configurations may omit their extractor. Preserve their established skip/default
        // behavior; modern editors explicitly persist the extraction mode and are validated.
        var strict = explicitMode.HasValue || basePath == PathMarkValueBasePath.ResourcePath;
        if (effectiveMode == PathMatchMode.Layer)
        {
            if (strict && !layer.HasValue) throw new ArgumentException("A value layer is required.");
            if (basePath == PathMarkValueBasePath.ResourcePath && layer is > 0)
                throw new ArgumentException("Resource-relative value layers must be zero or negative.");
        }
        else if (effectiveMode == PathMatchMode.Regex)
        {
            if (strict && string.IsNullOrWhiteSpace(regex))
                throw new ArgumentException("A value regex is required.");
            if (strict && !legacyRegex && !string.IsNullOrEmpty(regex)) _ = new Regex(regex);
        }
        else if (strict)
        {
            throw new ArgumentException("Unknown dynamic value extraction mode.");
        }
    }
}
