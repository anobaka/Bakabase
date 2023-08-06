using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures
{
    public class Enhancement
    {
        public EnhancementType Type
        {
            get
            {
                if (Key != null)
                {
                    if (Key.StartsWith("p:"))
                    {
                        return EnhancementType.Property;
                    }

                    if (Key.StartsWith("f:"))
                    {
                        return EnhancementType.File;
                    }
                }

                throw new Exception($"Unknown key prefix: {Key}");
            }
        }

        /// <summary>
        /// p:{Key} for property and f:{Key} for file
        /// </summary>
        public string Key { get; set; }

        public object Data { get; set; }

        public override string ToString()
        {
            switch (Type)
            {
                case EnhancementType.Property:
                    return $"[{Key}] {JsonConvert.SerializeObject(Data)}";
                case EnhancementType.File:
                    switch (Data)
                    {
                        case EnhancementFile f:
                            return $"[{Key}]{f.RelativePath}[{f.Data?.Length ?? 0}bytes]";
                        case IEnumerable<EnhancementFile> fs:
                            var fileInfoList = fs.Select(f => $"{f.RelativePath}[{f.Data.Length}bytes]");
                            var messages = new List<string>
                            {
                                $"[{Key}]"
                            };
                            messages.AddRange(fileInfoList);
                            return string.Join(Environment.NewLine, messages);
                    }

                    return $"[{Key}]Empty";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static Enhancement BuildReservedProperty<TData>(ReservedResourceProperty property, TData data)
        {
            return new Enhancement
            {
                Key = $"p:{property}",
                Data = data
            };
        }

        public static CustomEnhancement BuildCustomProperty(string property, CustomDataType dataType, object data,
            bool isArray)
        {
            if (!property.StartsWith("p:"))
            {
                property = $"p:{property}";
            }

            return new CustomEnhancement
            {
                Key = property,
                DataType = dataType,
                Data = data,
                IsArray = isArray
            };
        }

        public static Enhancement BuildReservedFile(ReservedResourceFileType type, EnhancementFile data)
        {
            return new Enhancement
            {
                Key = $"f:{type}",
                Data = data
            };
        }

        public static Enhancement BuildReservedFile(ReservedResourceFileType type, IEnumerable<EnhancementFile> data)
        {
            return new Enhancement
            {
                Key = $"f:{type}",
                Data = data
            };
        }

        public static Enhancement BuildCustomFile(string type, IEnumerable<EnhancementFile> files)
        {
            if (!type.StartsWith("f:"))
            {
                type = $"f:{type}";
            }

            return new Enhancement
            {
                Key = type,
                Data = files
            };
        }

        public static Enhancement BuildReleaseDt(DateTime dt) =>
            BuildReservedProperty(ReservedResourceProperty.ReleaseDt, dt);

        public static Enhancement BuildPublisher(IEnumerable<PublisherDto> publishers) =>
            BuildReservedProperty(ReservedResourceProperty.Publisher, publishers);

        public static Enhancement BuildName(string name) => BuildReservedProperty(ReservedResourceProperty.Name, name);

        public static Enhancement BuildVolume(VolumeDto volume) =>
            BuildReservedProperty(ReservedResourceProperty.Volume, volume);

        public static Enhancement BuildOriginal(IEnumerable<OriginalDto> originals) =>
            BuildReservedProperty(ReservedResourceProperty.Original, originals);

        public static Enhancement BuildSeries(SeriesDto series) =>
            BuildReservedProperty(ReservedResourceProperty.Series, series);

        public static Enhancement BuildIntroduction(string introduction) =>
            BuildReservedProperty(ReservedResourceProperty.Introduction, introduction);

        public static Enhancement BuildRate(decimal rate) => BuildReservedProperty(ReservedResourceProperty.Rate, rate);

        public static Enhancement BuildTag(IEnumerable<TagDto> tags) =>
            BuildReservedProperty(ReservedResourceProperty.Tag, tags);

        public static Enhancement BuildLanguage(ResourceLanguage language) =>
            BuildReservedProperty(ReservedResourceProperty.Language, language);
    }
}