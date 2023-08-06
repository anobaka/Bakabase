using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;
using Bootstrap.Models;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
{
    [Enhancer(new object[]
        {
            ReservedResourceProperty.Name,
            ReservedResourceProperty.Series,
            ReservedResourceProperty.Publisher,
            ReservedResourceProperty.ReleaseDt,
            ReservedResourceProperty.Volume,
            ReservedResourceProperty.Original,
            ReservedResourceProperty.Language,
        }, null,
        Description =
            "Parse name which likes [(20)191202][Publisher1(Publisher2,Publisher3),Publisher4(Publisher5),Publisher6]Title Part 2(Original)[CN]",
        Version = "1.0.1")]
    public class InsideWorldEnhancer : IEnhancer
    {
        private readonly ResourceService _resourceService;
        private readonly SpecialTextService _specialTextService;

        public InsideWorldEnhancer(ResourceService resourceService, SpecialTextService specialTextService)
        {
            _resourceService = resourceService;
            _specialTextService = specialTextService;
        }
        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        public async Task<Enhancement[]> Enhance(ResourceDto resource)
        {
            var name = resource.RawName;
            if (name.IsNullOrEmpty())
            {
                throw new ArgumentNullException(nameof(name));
            }

            var enhancements = new List<Enhancement>();
            if (IsValid(name))
            {
                // ReleaseDt and Language can be matched simply
                var allWcs = await _specialTextService.MatchAllContentsWithWrappers(name);

                var nameRef = new RefWrapper<string>(name);
                var releaseDt = await GetAndRemoveReleaseDt(nameRef, allWcs);
                name = nameRef.Value;

                var language = GetAndRemoveLanguageWithWrapper(ref name, allWcs);

                var publishers = GetAndRemovePublishers(ref name);
                var originals = GetAndRemoveOriginals(ref name);
                var volume = await _specialTextService.TryToParseVolume(name);
                if (volume != null)
                {
                    name = name[..volume.Value.Index];
                }

                if (releaseDt.HasValue)
                {
                    enhancements.Add(Enhancement.BuildReleaseDt(releaseDt.Value));
                }

                if (language != ResourceLanguage.NotSet)
                {
                    enhancements.Add(Enhancement.BuildLanguage(language));
                }

                if (name.IsNotEmpty())
                {
                    enhancements.Add(Enhancement.BuildName(name));
                }

                if (originals?.Any() == true)
                {
                    enhancements.Add(Enhancement.BuildOriginal(originals));
                }

                if (publishers?.Any() == true)
                {
                    enhancements.Add(Enhancement.BuildPublisher(publishers));
                }

                if (volume != null)
                {
                    enhancements.Add(Enhancement.BuildVolume(volume.Value.Volume));
                }
            }

            return enhancements.ToArray();
        }

        private async Task<DateTime?> GetAndRemoveReleaseDt(RefWrapper<string> name,
            IEnumerable<SpecialTextService.WrappedContent> wcs)
        {
            foreach (var wc in wcs)
            {
                var dt = await _specialTextService.TryToParseDateTime(wc.Content);
                if (dt.HasValue)
                {
                    name.Value = $"{name.Value[..wc.Index]}{name.Value[(wc.Index + wc.ContentWithWrapper.Length)..]}";
                    return dt.Value;
                }
            }

            return null;
        }

        private ResourceLanguage GetAndRemoveLanguageWithWrapper(ref string name,
            SpecialTextService.WrappedContent[] wcs)
        {
            var languageWords = _specialTextService[SpecialTextType.Language]
                .ToDictionary(t => t.Value1, t => Enum.Parse<ResourceLanguage>(t.Value2));
            // var wrappers = _specialTextService[SpecialTextType.Wrapper];

            foreach (var wc in wcs)
            {
                foreach (var (reg, l) in languageWords)
                {
                    if (Regex.IsMatch(wc.Content, reg))
                    {
                        name = $"{name[..wc.Index]}{name[(wc.Index + wc.ContentWithWrapper.Length)..]}";
                        return l;
                    }
                }
            }

            return ResourceLanguage.NotSet;
        }

        public static List<PublisherDto> GetAndRemovePublishers(ref string name)
        {
            if (name.StartsWith('['))
            {
                var endIndex = name.IndexOf(']');
                if (endIndex > 0)
                {
                    var publisherString = name.Substring(1, endIndex - 1);
                    name = name[(endIndex + 1)..];
                    try
                    {
                        return publisherString.AnalyzePublishers();
                    }
                    catch (Exception e)
                    {
                        throw;
                    }
                }
            }

            return null;
        }

        public static List<OriginalDto> GetAndRemoveOriginals(ref string name)
        {
            if (name.EndsWith(')'))
            {
                var layer = 1;
                for (var i = name.Length - 2; i > -1; i--)
                {
                    var c = name[i];
                    if (c == ')')
                    {
                        layer++;
                    }

                    if (c == '(')
                    {
                        layer--;
                    }

                    if (layer == 0)
                    {
                        var originals = name.Substring(i + 1, name.Length - i - 2);
                        name = name.Substring(0, i);
                        return originals.Split(new[] {'、'}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => new OriginalDto {Name = t.Trim()}).ToList();
                    }
                }
            }

            return null;
        }

        private bool IsValid(string name)
        {
            var wrappers = _specialTextService[SpecialTextType.Wrapper];
            foreach (var wrapper in wrappers)
            {
                var left = wrapper.Value1;
                var right = wrapper.Value2;
                var layer = 0;
                for (var i = 0; i < name.Length; i++)
                {
                    if (i + left.Length <= name.Length && name.Substring(i, left.Length) == left)
                    {
                        layer++;
                    }

                    if (i + right.Length <= name.Length && name.Substring(i, right.Length) == right)
                    {
                        layer--;
                    }

                    if (layer < 0)
                    {
                        return false;
                    }
                }

                if (layer != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}