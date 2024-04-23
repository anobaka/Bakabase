// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Text.RegularExpressions;
// using System.Threading.Tasks;
// using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;
// using Bakabase.InsideWorld.Business.Components.Network;
// using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
// using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai;
// using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.RequestModels;
// using Bakabase.InsideWorld.Business.Configurations;
// using Bakabase.InsideWorld.Business.Resources;
// using Bakabase.InsideWorld.Business.Services;
// using Bakabase.InsideWorld.Models.Configs;
// using Bakabase.InsideWorld.Models.Constants;
// using Bakabase.InsideWorld.Models.Models.Dtos;
// using Bakabase.InsideWorld.Models.Models.Entities;
// using Bootstrap.Extensions;
// using Bootstrap.Models.Constants;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Options;
//
// namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
// {
//     [Enhancer(Description = "1. Cookie of ExHentai must be set in system properties.\n2. The first item of search result will be treated as matched resource.\n3. You can set excluding tags also.", Target = new Target())]
//     public class ExHentaiEnhancer : IEnhancer
//     {
//         private readonly ExHentaiClient _exHentaiClient;
//         private readonly IServiceProvider _services;
//         private readonly InsideWorldOptionsManagerPool _optionsManager;
//         private readonly SpecialTextService _specialTextService;
//         private const string UrlKeywordRegex = "[a-zA-Z0-9]{10,}";
//         private readonly InsideWorldLocalizer _localizer;
//
//         public ExHentaiEnhancer(ExHentaiClient exHentaiClient, IServiceProvider services,
//             InsideWorldOptionsManagerPool optionsManager, SpecialTextService specialTextService, InsideWorldLocalizer localizer)
//         {
//             _exHentaiClient = exHentaiClient;
//             _services = services;
//             _optionsManager = optionsManager;
//             _specialTextService = specialTextService;
//             _localizer = localizer;
//         }
//
//         public TargetAttribute[] Targets =>
//         [
//             new TargetAttribute(_localizer.Tags(), StandardValueType.MultilevelText),
//             new TargetAttribute(_localizer.Rating(), StandardValueType.Rating)
//         ];
//
//         public async Task<Enhancement[]> Enhance(Models.Domain.Resource resource)
//         {
//             var options =_optionsManager.ExHentai;
//
//             var enhancements = new List<Enhancement>();
//
//             var name = resource.Name ?? resource.FileName;
//             var urlKeywords = new HashSet<string>();
//
//             var names = new List<string> { resource.Name, resource.FileName }.Where(a => a.IsNotEmpty()).ToArray();
//             if (names.Any(n => Regex.IsMatch(n, UrlKeywordRegex)))
//             {
//                 var texts = await _specialTextService.GetAll();
//                 if (texts.TryGetValue(SpecialTextType.Wrapper, out var wrappers))
//                 {
//                     var urlKeywordCandidates = new List<(string Str, string Keyword)>();
//                     foreach (var wrapper in wrappers)
//                     {
//                         var el = Regex.Escape(wrapper.Value1);
//                         var er = Regex.Escape(wrapper.Value2);
//                         foreach (var n in names)
//                         {
//                             var match = Regex.Match(n, $"{el}(?<k>{UrlKeywordRegex}){er}");
//                             if (match.Success)
//                             {
//                                 urlKeywordCandidates.AddRange(match.Groups["k"].Captures.Select(a => a.Value)
//                                     .Select(a => (Str: $"{wrapper.Value1}{a}{wrapper.Value2}", Keyword: a)));
//                             }
//                         }
//                     }
//
//                     foreach (var (str, keyword) in urlKeywordCandidates)
//                     {
//                         name = name.Replace(str, null);
//                         urlKeywords.Add(keyword);
//                     }
//                 }
//             }
//
//             var searchRsp = await _exHentaiClient.Search(
//                 new ExHentaiSearchRequestModel { Keyword = name, PageIndex = 1, PageSize = 1 });
//
//             var targetUrl = searchRsp.Resources?.FirstOrDefault()?.Url;
//             if (searchRsp?.Resources?.Count > 1 && urlKeywords.Any())
//             {
//                 targetUrl = searchRsp.Resources.FirstOrDefault(a => urlKeywords.Any(b => a.Url.Contains(b)))?.Url ??
//                             targetUrl;
//             }
//
//             if (targetUrl != null)
//             {
//                 var detail = await _exHentaiClient.ParseDetail(targetUrl);
//                 if (detail != null)
//                 {
//                     if (detail.Introduction.IsNotEmpty())
//                     {
//                         enhancements.Add(Enhancement.BuildIntroduction(detail.Introduction));
//                     }
//
//                     if (detail.Tags.IsNotEmpty())
//                     {
//                         var tagGroups = detail.Tags.ToDictionary(t => t.Key, t => t.Value.ToList());
//                         if (options.Value.Enhancer?.ExcludedTags?.Any() == true)
//                         {
//                             foreach (var t in options.Value.Enhancer.ExcludedTags.Where(t => t.IsNotEmpty()))
//                             {
//                                 var segments = t.Split(':', StringSplitOptions.RemoveEmptyEntries);
//
//                                 if (segments.Length > 1)
//                                 {
//                                     var group = segments[0];
//                                     var tag = segments[1];
//
//                                     var groups = group == "*" ? tagGroups.Keys.ToArray() : new[] { group };
//                                     foreach (var g in groups)
//                                     {
//                                         if (tagGroups.TryGetValue(g, out var tags))
//                                         {
//                                             if (tag != "*")
//                                             {
//                                                 tagGroups[g] = tags.Where(x => x != tag).ToList();
//                                             }
//                                             else
//                                             {
//                                                 tagGroups.Remove(g);
//                                             }
//                                         }
//                                     }
//                                 }
//                             }
//                         }
//
//                         enhancements.Add(Enhancement.BuildTag(tagGroups.SelectMany(t => t.Value.Select(a => new TagDto
//                             {
//                                 Name = a,
//                                 GroupName = t.Key
//                             }))));
//                     }
//
//                     if (detail.Rate > 0)
//                     {
//                         enhancements.Add(Enhancement.BuildRate(detail.Rate));
//                     }
//                 }
//             }
//
//             return enhancements.ToArray();
//         }
//
//         public async Task<string> Validate()
//         {
//             var options =_optionsManager.ExHentai;
//             return options.Value.Cookie.IsNotEmpty() ? null : "Cookie of ExHentai is not set";
//         }
//     }
// }