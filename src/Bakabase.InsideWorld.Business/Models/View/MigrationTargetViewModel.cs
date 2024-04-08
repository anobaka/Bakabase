using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Models.View
{
    public record MigrationTargetViewModel : MigrationTarget
    {
        public List<PropertyTypeCandidate> TargetCandidates { get; set; } = null!;

        public record PropertyTypeCandidate
        {
            public CustomPropertyType Type { get; set; }
            public string? Message { get; set; }
            public List<string>? BadData { get; set; }
        }


        public static MigrationTargetViewModel BuildSimpleValue<T>(List<T> data, ResourceProperty property,
            string? propertyKey, CustomPropertyType[] typeCandidates)
        {
            var distinctData = data.Distinct().ToArray();
            var model = new MigrationTargetViewModel
            {
                Data = distinctData,
                DataCount = data.Count,
                Property = property,
                PropertyKey = propertyKey,
                TargetCandidates = []
            };

            var tType = SpecificTypeUtils<T>.Type;

            foreach (var tc in typeCandidates)
            {
                var candidate = new PropertyTypeCandidate
                {
                    Type = tc
                };

                if (tType == SpecificTypeUtils<string>.Type)
                {
                    switch (tc)
                    {
                        case CustomPropertyType.SingleLineText:
                            break;
                        case CustomPropertyType.MultilineText:
                            break;
                        case CustomPropertyType.SingleChoice:
                            break;
                        case CustomPropertyType.MultipleChoice:
                            break;
                        case CustomPropertyType.Number:
                        case CustomPropertyType.Percentage:
                        case CustomPropertyType.Rating:
                        {
                            candidate.BadData = distinctData.Where(x => !decimal.TryParse(x as string, out var d))
                                .Select(d => d!.ToString()!).ToList();
                            if (candidate.BadData.Any())
                            {
                                candidate.Message = "Some values can not be converted to a number will be discarded.";
                            }
                            break;
                        }
                        case CustomPropertyType.Boolean:
                            break;
                        case CustomPropertyType.Link:
                            break;
                        case CustomPropertyType.Attachment:
                            break;
                        case CustomPropertyType.Date:
                            break;
                        case CustomPropertyType.DateTime:
                            break;
                        case CustomPropertyType.Time:
                            break;
                        case CustomPropertyType.Formula:
                            break;
                        case CustomPropertyType.Multilevel:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    if (tType == SpecificTypeUtils<decimal>.Type)
                    {

                    }
                    else
                    {
                        if (tType == SpecificTypeUtils<DateTime>.Type)
                        {

                        }
                    }
                }

                if (candidate.BadData?.Any() == true)
                {

                }

                model.TargetCandidates.Add(candidate);
            }

            return model;
        }

        public static MigrationTargetViewModel BuildVolume(List<VolumeDto> volumes)
        {
            var indexes = volumes.Select(v => v.Index).ToHashSet();
            var titles = volumes.Select(v => v.Title).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
            var names = volumes.Select(v => v.Name).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();

            var subTargets = new List<MigrationTarget>();

            if (indexes.Any())
            {
                subTargets.Add(BuildSimpleValue(indexes.ToList(), ResourceProperty.Volume, nameof(VolumeDto.Index),
                    [CustomPropertyType.Number]));
            }

            if (titles.Any())
            {
                subTargets.Add(BuildSimpleValue(titles.ToList(), ResourceProperty.Volume, nameof(VolumeDto.Title),
                    [CustomPropertyType.SingleLineText]));
            }

            if (names.Any())
            {
                subTargets.Add(BuildSimpleValue(names.ToList(), ResourceProperty.Volume, nameof(VolumeDto.Name),
                    [CustomPropertyType.SingleLineText])););
            }

            return new MigrationTargetViewModel
            {
                Property = ResourceProperty.Volume,
                DataCount = volumes.Count,
                SubTargets = subTargets
            };
        }

        public static MigrationTargetViewModel BuildCustomResourceProperties(
            List<CustomResourceProperty> customResourceProperties)
        {
            var customPropertiesGroups = customResourceProperties.GroupBy(x => x.ResourceId)
                .SelectMany(x => x.GroupBy(y => y.Key)).GroupBy(x => x.Key, x => x.ToList()).ToList();
            var allCandidates = SpecificEnumUtils<CustomDataType>.Values.ToArray();
            return new MigrationTargetViewModel
            {
                Property = ResourceProperty.CustomProperty,
                DataCount = customPropertiesGroups.Sum(x => x.Count()),
                // SubTargets = customPropertiesGroups.Select(x => new MigrationTarget
                // {
                //     PropertyKey = x.Key,
                //     Data = x.Select(y => y.Select(z => z.Value)).ToList(),
                //     DataCount = x.Count()
                // })
                SubTargets = customPropertiesGroups.Select(x =>
                    BuildSimpleValue(x.SelectMany(y => y.Select(z => z.Value)).ToList(),
                        ResourceProperty.CustomProperty, x.Key, allCandidates))
            };
        }
    }
}