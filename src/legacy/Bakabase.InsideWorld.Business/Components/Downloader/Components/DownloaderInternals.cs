using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

public class DownloaderInternals
{
    public static List<DownloaderDefinition> Definitions { get; } =
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return [];
                }
            })
            .Where(t => t.IsEnum)
            .SelectMany(enumType =>
                enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.GetCustomAttribute<DownloaderAttribute>() != null)
                    .Select(field =>
                    {
                        var attribute = field.GetCustomAttribute<DownloaderAttribute>()!;
                        var enumValue = field.GetValue(null)!;

                        // Extract naming fields from the naming fields type
                        var namingFields = new List<DownloaderDefinition.Field>();
                        if (attribute.NamingFieldsType.IsEnum)
                        {
                            namingFields = attribute.NamingFieldsType
                                .GetFields(BindingFlags.Public | BindingFlags.Static)
                                .Where(f => f.IsLiteral)
                                .Select(f => new DownloaderDefinition.Field
                                {
                                    Key = f.Name,
                                    Name = f.Name,
                                    Description = null,
                                    Example = null,
                                    EnumValue = f.GetValue(null)!,
                                })
                                .ToList();
                        }

                        return new DownloaderDefinition
                        {
                            ThirdPartyId = attribute.ThirdPartyId,
                            TaskType = (int)enumValue,
                            Name = field.Name,
                            Description = $"{field.Name} downloader for {attribute.ThirdPartyId}",
                            DownloaderType = attribute.Type,
                            HelperType = attribute.HelperType,
                            DefaultConvention = attribute.DefaultNamingConvention,
                            NamingFields = namingFields,
                            // NamingFieldEnumType = attribute.NamingFieldsType,
                            // TaskTypeEnumType = enumType,
                            EnumTaskType = enumValue
                        };
                    })
            )
            .ToList();

    public static ConcurrentDictionary<Type, DownloaderDefinition> DownloaderTypeDefinitionMap { get; } =
        new(Definitions.ToDictionary(d => d.DownloaderType, d => d));

    public static ConcurrentDictionary<ThirdPartyId, ConcurrentDictionary<int, DownloaderDefinition>>
        ThirdPartyIdTaskTypeDefinitionMap { get; } = new(Definitions.GroupBy(d => d.ThirdPartyId)
        .ToDictionary(g => g.Key, g => new ConcurrentDictionary<int, DownloaderDefinition>(
            g.ToDictionary(d => d.TaskType, d => d))));

}