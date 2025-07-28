using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Attribute to define downloader metadata for DownloaderSource enum values
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DownloaderAttribute(
        ThirdPartyId thirdPartyId,
        Type downloaderType,
        string defaultNamingConvention,
        Type namingFieldsType,
        Type helperType)
        : Attribute
    {
        public ThirdPartyId ThirdPartyId { get; } = thirdPartyId;

        /// <summary>
        /// Default naming convention for this downloader
        /// </summary>
        public string DefaultNamingConvention { get; } = defaultNamingConvention;

        /// <summary>
        /// Type of the naming fields enum for this downloader
        /// </summary>
        public Type NamingFieldsType { get; } = namingFieldsType;

        /// <summary>
        /// The downloader implementation type
        /// </summary>
        public Type Type { get; } = downloaderType;

        /// <summary>
        /// The helper implementation type that provides options and validation
        /// </summary>
        public Type HelperType { get; } = helperType;
    }
}