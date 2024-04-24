using System;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Extensions
{
    public static class NumberExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="layer">Starts from 1</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string BuildMultiLayerRegexString(this int layer)
        {
            if (!(layer > 0))
            {
                throw new ArgumentException(
                    $"{nameof(layer)} can be greater than zero only, and current value is {layer}");
            }

            var sb = new StringBuilder("^");
            for (var i = 0; i < layer; i++)
            {
                sb.Append(InternalOptions.RegexForOnePathLayer);
                if (i < layer - 1)
                {
                    sb.Append($"\\{InternalOptions.DirSeparator}");
                }
            }

            sb.Append("$");
            return sb.ToString();
        }
    }
}