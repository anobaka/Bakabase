using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Microsoft.OpenApi.Extensions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bakabase.InsideWorld.Models.Constants
{
    public class BusinessConstants
    {
        public static readonly ImmutableHashSet<string> ImageExtensions =
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
                ".bmp",
                ".gif",
                ".ico",
                ".jpeg",
                ".jpg",
                ".png",
                ".psd",
                ".tiff",
                ".webp",
                ".svg"
            );

        public static readonly ImmutableHashSet<string> VideoExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".mp4",
            ".avi",
            ".mkv",
            ".wmv",
            ".flv",
            ".ts",
            ".webm",
            ".mpeg",
            ".rmvb",
            "3gp",
            "mov"
        );

        public static readonly ImmutableHashSet<string> AudioExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".mp3",
            ".flac",
            ".m4a",
            ".mid",
            ".midi",
            ".ogg",
            ".wav",
            ".weba"
        );

        public static readonly ImmutableHashSet<string> CompressedFileExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".rar",
            ".7z",
            ".zip",
            ".tar",
            ".bz2",
            ".gz",
            ".tgz"
        );

        public static readonly ImmutableHashSet<string> TextExtensions = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            ".txt"
        );

        public const string SevenZipCompressedFileExtension = ".7z";
        public const string IcoFileExtension = ".ico";

        public const char DirSeparator = '/';
        public const char WindowsSpecificDirSeparator = '\\';
        public static string UncPathPrefix = $"{DirSeparator}{DirSeparator}";

        public static string WindowsSpecificUncPathPrefix =
            $"{WindowsSpecificDirSeparator}{WindowsSpecificDirSeparator}";

        public const char CompressedFileRootSeparator = '!';
        public const string RegexForOnePathLayer = @"[^\/]+";

        public static readonly Dictionary<ComponentType, ComponentTypeRequirement> ComponentTypeRequirements =
            new Dictionary<ComponentType, ComponentTypeRequirement>
            {
                {
                    ComponentType.Player, new ComponentTypeRequirement
                    {
                        MaxCount = 1,
                        Required = true
                    }
                },
                {
                    ComponentType.PlayableFileSelector, new ComponentTypeRequirement
                    {
                        MaxCount = 1,
                        Required = true
                    }
                },
                {
                    ComponentType.Enhancer, new ComponentTypeRequirement
                    {
                        MaxCount = int.MaxValue,
                        Required = false
                    }
                }
            };

        public static HashSet<string> IgnoredFileExtensions = new(StringComparer.OrdinalIgnoreCase)
            {".nfo"};

        public const string AppOssObjectPrefix = "app/bakabase/inside-world/";
        public const string DefaultName = "Default";
        public const string TagNameSeparator = ":";
        public const string TextSeparator = ",";
        public const string ExeExtension = ".exe";

        public class HttpClientNames
        {
            public const string JavLibrary = nameof(JavLibrary);
            public const string Bilibili = nameof(Bilibili);
            public const string ExHentai = nameof(ExHentai);
            public const string Pixiv = nameof(Pixiv);
            public const string Default = nameof(Default);
        }

        public static string DefaultHttpUserAgent =
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.71 Safari/537.36 Edg/94.0.992.38 InsideWorld/{AppService.CoreVersion}";

        public const string CoverDirectoryName = "cover";
        public const string TempDirectoryName = "temp";
        public const string ComponentInfoFileName = "i.json";

        public static readonly HashSet<ResourceProperty> PropertiesAppliedAliases =
        [
            ResourceProperty.Name,
            ResourceProperty.Tag
        ];


        public static readonly ConcurrentDictionary<SearchableReservedProperty, StandardValueType>
            ReservedResourcePropertyAndValueTypeMap =
                new(new Dictionary<SearchableReservedProperty, StandardValueType>()
                {
                    {SearchableReservedProperty.FileName, StandardValueType.SingleLineText},
                    {SearchableReservedProperty.DirectoryPath, StandardValueType.SingleLineText},
                    {SearchableReservedProperty.CreatedAt, StandardValueType.DateTime},
                    {SearchableReservedProperty.FileCreatedAt, StandardValueType.DateTime},
                    {SearchableReservedProperty.FileModifiedAt, StandardValueType.DateTime},
                    {SearchableReservedProperty.Tag, StandardValueType.MultipleChoice},
                    {SearchableReservedProperty.Category, StandardValueType.SingleChoice},
                    {SearchableReservedProperty.MediaLibrary, StandardValueType.SingleChoice},
                    {SearchableReservedProperty.Favorites, StandardValueType.SingleChoice},
                });

        public static readonly ConcurrentDictionary<StandardValueType, SearchOperation[]>
            StandardValueSearchOperationsMap =
                new(new Dictionary<StandardValueType[], SearchOperation[]>()
                {
                    {
                        [StandardValueType.SingleLineText, StandardValueType.MultilineText, StandardValueType.Link], [
                            SearchOperation.Equals,
                            SearchOperation.NotEquals,
                            SearchOperation.Contains,
                            SearchOperation.NotContains,
                            SearchOperation.StartsWith,
                            SearchOperation.NotStartsWith,
                            SearchOperation.EndsWith,
                            SearchOperation.NotEndsWith,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                            SearchOperation.Matches,
                            SearchOperation.NotMatches
                        ]
                    },
                    {
                        [StandardValueType.SingleChoice], [
                            SearchOperation.Equals,
                            SearchOperation.NotEquals,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                            SearchOperation.In,
                            SearchOperation.NotIn
                        ]
                    },
                    {
                        [StandardValueType.MultipleChoice], [
                            SearchOperation.Contains,
                            SearchOperation.NotContains,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull
                        ]
                    },
                    {
                        [StandardValueType.Number, StandardValueType.Percentage, StandardValueType.Rating], [
                            SearchOperation.Equals,
                            SearchOperation.NotEquals,
                            SearchOperation.GreaterThan,
                            SearchOperation.LessThan,
                            SearchOperation.GreaterThanOrEquals,
                            SearchOperation.LessThanOrEquals,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                        ]
                    },
                    {
                        [StandardValueType.Boolean], [
                            SearchOperation.Equals,
                            SearchOperation.NotEquals,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                        ]
                    },
                    {
                        [StandardValueType.Attachment], [
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                        ]
                    },
                    {
                        [StandardValueType.Date, StandardValueType.DateTime, StandardValueType.Time], [
                            SearchOperation.Equals,
                            SearchOperation.NotEquals,
                            SearchOperation.GreaterThan,
                            SearchOperation.LessThan,
                            SearchOperation.GreaterThanOrEquals,
                            SearchOperation.LessThanOrEquals,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull
                        ]
                    },
                    {
                        [StandardValueType.Formula], [
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                        ]
                    },
                    {
                        [StandardValueType.Multilevel], [
                            SearchOperation.Contains,
                            SearchOperation.NotContains,
                            SearchOperation.IsNull,
                            SearchOperation.IsNotNull,
                        ]
                    },
                }.SelectMany(x =>
                    x.Key.Select(a => new KeyValuePair<StandardValueType, SearchOperation[]>(a, x.Value))));

        public static readonly
            ConcurrentDictionary<StandardValueType,
                ConcurrentDictionary<StandardValueType, StandardValueConversionLoss>>
            StandardValueConversionLoss = new(
                new Dictionary<StandardValueType[],
                    Dictionary<StandardValueType, StandardValueConversionLoss>>
                {
                    {
                        [StandardValueType.SingleLineText, StandardValueType.MultilineText], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.SingleChoice], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.MultipleChoice], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.MultipleValuesWillBeMerged},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.MultipleValuesWillBeMerged},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.MultipleValuesWillBeMerged},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.MultipleValuesWillBeMerged},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {
                                StandardValueType.Number,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Percentage,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Rating,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {
                                StandardValueType.Date,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.DateTime,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Time,
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Number, StandardValueType.Percentage, StandardValueType.Rating], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NonZeroValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Boolean], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Link], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.TextWillBeLost},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.TextWillBeLost},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.TextWillBeLost},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.TextWillBeLost},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.TextWillBeLost},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Attachment], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Date], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.DateTime], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.TimeWillBeLost},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.DateWillBeLost},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Time], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Formula], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Number, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Percentage, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Rating, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Date, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.DateTime, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Time, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.None},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.All},
                        }
                    },
                    {
                        [StandardValueType.Multilevel], new()
                        {
                            {StandardValueType.SingleLineText, Constants.StandardValueConversionLoss.ChildrenWillBeLost},
                            {StandardValueType.MultilineText, Constants.StandardValueConversionLoss.ChildrenWillBeLost},
                            {StandardValueType.Link, Constants.StandardValueConversionLoss.ChildrenWillBeLost},
                            {StandardValueType.SingleChoice, Constants.StandardValueConversionLoss.ChildrenWillBeLost},
                            {StandardValueType.MultipleChoice, Constants.StandardValueConversionLoss.ChildrenWillBeLost},
                            {
                                StandardValueType.Number,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Percentage,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Rating,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {StandardValueType.Boolean, Constants.StandardValueConversionLoss.NotEmptyValueWillBeConvertedToTrue},
                            {StandardValueType.Attachment, Constants.StandardValueConversionLoss.All},
                            {
                                StandardValueType.Date,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.DateTime,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {
                                StandardValueType.Time,
                                Constants.StandardValueConversionLoss.ChildrenWillBeLost |
                                Constants.StandardValueConversionLoss.InconvertibleDataWillBeLost |
                                Constants.StandardValueConversionLoss.OnlyFirstValueWillBeRemained
                            },
                            {StandardValueType.Formula, Constants.StandardValueConversionLoss.All},
                            {StandardValueType.Multilevel, Constants.StandardValueConversionLoss.None},
                        }
                    },
                }.SelectMany(x => x.Key.Select(a =>
                    new KeyValuePair<StandardValueType,
                        ConcurrentDictionary<StandardValueType, StandardValueConversionLoss>>(a, new(x.Value)))));
    }
}