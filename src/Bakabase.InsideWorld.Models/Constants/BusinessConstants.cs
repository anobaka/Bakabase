using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Models.Attributes;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Microsoft.OpenApi.Extensions;

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


		public static readonly ConcurrentDictionary<ResourceProperty, StandardValueType>
			ReservedResourcePropertyAndValueTypeMap =
				new(new Dictionary<ResourceProperty, StandardValueType>()
				{
					{ResourceProperty.Name, StandardValueType.SingleLineText},
					{ResourceProperty.FileName, StandardValueType.SingleLineText},
					{ResourceProperty.DirectoryName, StandardValueType.SingleLineText},
					{ResourceProperty.DirectoryPath, StandardValueType.SingleLineText},
					{ResourceProperty.CreatedAt, StandardValueType.DateTime},
					{ResourceProperty.ModifiedAt, StandardValueType.DateTime},
					{ResourceProperty.FileCreatedAt, StandardValueType.DateTime},
					{ResourceProperty.FileModifiedAt, StandardValueType.DateTime},
					{ResourceProperty.Tag, StandardValueType.MultipleChoice},
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
	}
}