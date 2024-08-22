using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class StringExtensions
    {
        private static readonly Regex CompressedFilePasswordRegex = new Regex(@"\[(?<p>[^\]\[]+)\]");

        public static string[] GetPasswordsFromPathWithoutExtension(this string pathWithoutExtension)
        {
            var matches = CompressedFilePasswordRegex.Matches(pathWithoutExtension);
            var passwords = matches.Where(a => a.Success).Select(a => a.Groups["p"].Value).Reverse().Distinct().ToArray();
            return passwords;
        }

        public static string[] GetPasswordsFromPath(this string path)
        {
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext))
            {
                path = path[..^ext.Length];
            }

            return GetPasswordsFromPathWithoutExtension(path);
        }

        public static MediaType InferMediaType(this string? path)
        {
            if (path.IsNullOrEmpty())
            {
                return MediaType.Unknown;
            }

            var ext = Path.GetExtension(path);
            if (ext.IsNullOrEmpty())
            {
                return MediaType.Unknown;
            }

            if (InternalOptions.ImageExtensions.Contains(ext))
            {
                return MediaType.Image;
            }

            if (InternalOptions.VideoExtensions.Contains(ext))
            {
                return MediaType.Video;
            }

            if (InternalOptions.TextExtensions.Contains(ext))
            {
                return MediaType.Text;
            }

            if (InternalOptions.AudioExtensions.Contains(ext))
            {
                return MediaType.Audio;
            }

            return MediaType.Unknown;
        }

        public static string[] SplitPathIntoSegments(this string path)
        {
            var sp = path.StandardizePath()!;
            var segments = sp.Split(InternalOptions.DirSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (path.StartsWith(InternalOptions.UncPathPrefix))
            {
                segments[0] = $"{InternalOptions.UncPathPrefix}{segments[0]}";
            }

            return segments;
        }

        

        /// <summary>
        /// 
        /// </summary>
        /// <param name="regex"></param>
        /// <param name="layer">Starts from 1</param>
        /// <returns></returns>
        public static bool TryGetLayer(this string? regex, out int layer)
        {
            layer = 0;
            if (regex.IsNullOrEmpty())
            {
                return false;
            }

            // try top 10 layers
            for (var i = 1; i <= 10; i++)
            {
                var standardRegex = i.BuildMultiLayerRegexString();
                if (regex == standardRegex)
                {
                    layer = i;
                    return true;
                }
            }

            return false;
        }


    }
}