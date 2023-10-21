using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using JetBrains.Annotations;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class StringExtensions
    {
        private static readonly char[] PathSeparatorCandidates =
        {
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar,
            // Path.VolumeSeparatorChar, 
            // Path.PathSeparator
        };

        public static string[] SplitPathIntoSegments(this string path)
        {
            var sp = path.StandardizePath()!;
            var segments = sp.Split(BusinessConstants.DirSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (path.StartsWith(BusinessConstants.UncPathPrefix))
            {
                segments[0] = $"{BusinessConstants.UncPathPrefix}{segments[0]}";
            }

            return segments;
        }

        public static string? StandardizePath(this string? path)
        {
            if (path.IsNullOrEmpty())
            {
                return null;
            }

            var sp = string.Join(BusinessConstants.DirSeparator,
                path!.Split(PathSeparatorCandidates, StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.TrimEnd())
                    .Where(a => a.IsNotEmpty()));

            // windows drive
            if (sp.EndsWith(':'))
            {
                sp += '/';
            }

            if (path.IsUncPath())
            {
                sp = BusinessConstants.UncPathPrefix + sp;
            }

            return sp;
        }

        public static bool IsUncPath(this string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.StartsWith(BusinessConstants.UncPathPrefix))
            {
                path = $"{BusinessConstants.WindowsSpecificUncPathPrefix}{path[2..]}";
            }

            try
            {
                var uri = new Uri(path);
                return uri.IsUnc;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="regex"></param>
        /// <param name="layer">Starts from 1</param>
        /// <returns></returns>
        public static bool TryGetLayer([CanBeNull] this string regex, out int layer)
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

            if (BusinessConstants.ImageExtensions.Contains(ext))
            {
                return MediaType.Image;
            }

            if (BusinessConstants.VideoExtensions.Contains(ext))
            {
                return MediaType.Video;
            }

            if (BusinessConstants.TextExtensions.Contains(ext))
            {
                return MediaType.Text;
            }

            if (BusinessConstants.AudioExtensions.Contains(ext))
            {
                return MediaType.Audio;
            }

            return MediaType.Unknown;
        }
    }
}