using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Models.Dtos
{
	public class ResourceDto
	{
		public int Id { get; set; }

		public string Directory { get; set; } = null!;
		public DateTime? ReleaseDt { get; set; }

		/// <summary>
		/// Business name of resource, default value is null.
		/// </summary>
		public string? Name { get; set; }

		/// <summary>
		/// Comic, Anime, Video, etc.
		/// </summary>
		public VolumeDto? Volume { get; set; }

		public ResourceLanguage Language { get; set; } = ResourceLanguage.NotSet;

		/// <summary>
		/// Picture可能没有
		/// </summary>
		public List<PublisherDto>? Publishers { get; set; }

		/// <summary>
		/// Comic, Anime, etc.
		/// </summary>
		public List<OriginalDto>? Originals { get; set; }

		public string DisplayName => this.BuildFullname();

		/// <summary>
		/// Raw filename
		/// </summary>
		public string RawName { get; set; } = null!;

		public decimal Rate { get; set; }
		public bool IsSingleFile { get; set; }
		public int? ParentId { get; set; }
		public bool HasChildren { get; set; }

		/// <summary>
		/// Fullname in filesystem.
		/// </summary>
		public string RawFullname
		{
			get
			{
				if (Directory.IsNullOrEmpty() && RawName.IsNullOrEmpty())
				{
					return null;
				}

				return Path.Combine(new[] {Directory, RawName}.Where(a => !string.IsNullOrEmpty(a)).ToArray())
					.StandardizePath()!;
			}
		}

		/// <summary>
		/// Comic, etc.
		/// </summary>
		public SeriesDto? Series { get; set; }

		/// <summary>
		/// todo: Picture的Tag会有很多从文件名获取
		/// </summary>
		public List<TagDto>? Tags { get; set; }

		public string? Introduction { get; set; }
		public int MediaLibraryId { get; set; }
		public int CategoryId { get; set; }
		public DateTime CreateDt { get; set; } = DateTime.Now;

		public DateTime UpdateDt { get; set; } = DateTime.Now;

		// public string ResolverVersion { get; set; }
		public DateTime FileCreateDt { get; set; }
		public DateTime FileModifyDt { get; set; }
		public Dictionary<string, List<CustomResourceProperty>>? CustomProperties { get; set; }
		public ResourceDto? Parent { get; set; }

		public List<CustomPropertyDto>? CustomPropertiesV2 { get; set; }
		public List<CustomPropertyValueDto?>? CustomPropertyValues { get; set; }
	}
}