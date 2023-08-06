using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    }
}