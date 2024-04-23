// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text.RegularExpressions;
// using System.Threading.Tasks;
// using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.InsideWorld.Models.Components;
// using Bakabase.InsideWorld.Models.Configs.CustomOptions;
// using Bakabase.InsideWorld.Models.Constants;
// using Bakabase.InsideWorld.Models.Models.Dtos;
// using NPOI.SS.Formula.Functions;
//
// namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
// {
//     [Enhancer(new object[]
//     {
//         ReservedResourceProperty.ReleaseDt,
//         ReservedResourceProperty.Publisher,
//         ReservedResourceProperty.Name,
//         ReservedResourceProperty.Language,
//         ReservedResourceProperty.Volume,
//         ReservedResourceProperty.Original,
//         ReservedResourceProperty.Series,
//         ReservedResourceProperty.Tag,
//         ReservedResourceProperty.Introduction,
//         ReservedResourceProperty.Rate,
//     }, null, OptionsType = typeof(RegexEnhancerOptions))]
//     public class RegexEnhancer : IEnhancer
//     {
//         private readonly RegexEnhancerOptions _options;
//         private readonly SpecialTextService _specialTextService;
//
//         public RegexEnhancer(RegexEnhancerOptions options, SpecialTextService specialTextService)
//         {
//             _options = options;
//             _specialTextService = specialTextService;
//         }
//
//         public Task<string> Validate()
//         {
//             return Task.FromResult<string>(null);
//         }
//
//         public async Task<Enhancement[]> Enhance(Business.Models.Domain.Resource resource)
//         {
//             var rawName = resource.FileName;
//             var match = Regex.Match(rawName, _options.Regex, RegexOptions.IgnoreCase);
//
//             if (match.Success)
//             {
//                 var enhancements = new List<Enhancement>();
//                 foreach (var g in _options.Groups)
//                 {
//                     var group = match.Groups[g.Name];
//                     if (group.Success)
//                     {
//                         if (g.IsReserved)
//                         {
//                             var p = Enum.Parse<ReservedResourceProperty>(g.Key);
//                             switch (p)
//                             {
//                                 case ReservedResourceProperty.ReleaseDt:
//                                 {
//                                     var dt = await _specialTextService.TryToParseDateTime(group.Value);
//                                     if (dt.HasValue)
//                                     {
//                                         enhancements.Add(Enhancement.BuildReleaseDt(dt.Value));
//                                     }
//
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Publisher:
//                                 {
//                                     var publishers = group.Captures.Select(x => new PublisherDto() {Name = x.Value})
//                                         .ToArray();
//                                     if (publishers.Any())
//                                     {
//                                         enhancements.Add(Enhancement.BuildPublisher(publishers));
//                                     }
//
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Name:
//                                 {
//                                     enhancements.Add(Enhancement.BuildName(group.Value));
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Language:
//                                 {
//                                     var language = await _specialTextService.TryToParseLanguage(group.Value);
//                                     if (language.HasValue)
//                                     {
//                                         enhancements.Add(Enhancement.BuildLanguage(language.Value));
//                                     }
//
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Volume:
//                                 {
//                                     var v = await _specialTextService.TryToParseVolume(group.Value);
//                                     if (v.HasValue)
//                                     {
//                                         enhancements.Add(Enhancement.BuildVolume(v.Value.Volume));
//                                     }
//
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Original:
//                                 {
//                                     var originals = group.Captures.Select(x => new OriginalDto
//                                     {
//                                         Name = x.Value
//                                     });
//                                     enhancements.Add(Enhancement.BuildOriginal(originals));
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Series:
//                                 {
//                                     enhancements.Add(Enhancement.BuildSeries(new SeriesDto {Name = group.Value}));
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Tag:
//                                 {
//                                     var tags = group.Captures.Select(a => new TagDto
//                                     {
//                                         Name = a.Value
//                                     }).ToArray();
//                                     enhancements.Add(Enhancement.BuildTag(tags));
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Introduction:
//                                 {
//                                     enhancements.Add(Enhancement.BuildIntroduction(group.Value));
//                                     break;
//                                 }
//                                 case ReservedResourceProperty.Rate:
//                                 {
//                                     if (decimal.TryParse(group.Value, out var r))
//                                     {
//                                         enhancements.Add(Enhancement.BuildRate(r));
//                                     }
//
//                                     break;
//                                 }
//                                 default:
//                                     throw new ArgumentOutOfRangeException();
//                             }
//                         }
//                         else
//                         {
//                             enhancements.Add(Enhancement.BuildCustomProperty(g.Key, CustomDataType.String, group.Value,
//                                 false));
//                         }
//                     }
//                 }
//
//                 return enhancements.ToArray();
//             }
//
//             return Array.Empty<Enhancement>();
//         }
//     }
// }