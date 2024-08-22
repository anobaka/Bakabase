using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.Constants
{
    /// <summary>
    /// DO NOT CHANGE ORDERS
    /// </summary>
    [Obsolete]
    public enum ReservedResourceProperty
    {
        /// <summary>
        /// <see cref="DateTime"/>
        /// </summary>
        ReleaseDt = 1,
        /// <summary>
        /// <see cref="IEnumerable{T}"/> and <see cref="PublisherDto"/>
        /// </summary>
        Publisher,
        Name,
        /// <summary>
        /// <see cref="ResourceLanguage"/>
        /// </summary>
        Language,
        /// <summary>
        /// <see cref="VolumeDto"/>
        /// </summary>
        Volume,
        /// <summary>
        /// <see cref="IEnumerable{T}"/> and <see cref="OriginalDto"/>
        /// </summary>
        Original,
        /// <summary>
        /// <see cref="SeriesDto"/>
        /// </summary>
        Series,
        /// <see cref="IEnumerable{T}"/> and <see cref="TagDto"/>
        Tag,
        Introduction,
        Rate
    }
}