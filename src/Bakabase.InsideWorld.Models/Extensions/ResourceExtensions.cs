using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading;
using System.Threading.Channels;
using Bakabase.InsideWorld.Models.Components;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Newtonsoft.Json;
using SharpCompress.Common;

namespace Bakabase.InsideWorld.Models.Extensions
{
	public static class ResourceExtensions
	{
		#region Helpers

		public static bool IsListProperty(this ResourceDiffProperty property)
		{
			return property switch
			{
				ResourceDiffProperty.Publisher => true,
				ResourceDiffProperty.Tag => true,
				ResourceDiffProperty.Original => true,
				ResourceDiffProperty.CustomProperty => true,
				ResourceDiffProperty.Name => false,
				ResourceDiffProperty.Language => false,
				ResourceDiffProperty.Volume => false,
				ResourceDiffProperty.Series => false,
				ResourceDiffProperty.Introduction => false,
				ResourceDiffProperty.Category => false,
				ResourceDiffProperty.MediaLibrary => false,
				ResourceDiffProperty.ReleaseDt => false,
				ResourceDiffProperty.Rate => false,
				_ => false
			};
		}

		public static bool HasAppliedAliases(this ResourceProperty rp)
		{
			return BusinessConstants.PropertiesAppliedAliases.Contains(rp);
		}

		#endregion

		#region Strings

		// public static string BuildFullname(this Resource resource, bool logical = false)
		// {
		// 	var releaseDate = resource.ReleaseDt.BuildReleaseDtString();
		// 	var publisher = resource.Publishers.BuildPublisherString();
		// 	var origin = resource.Originals.BuildOriginalsString();
		// 	var language = resource.Language.BuildLanguageString();
		// 	var volume = resource.Volume.BuildVolumeString();
		//
		// 	var fullname = $"{releaseDate}{publisher}{resource.Name ?? resource.RawName}{volume}{origin}{language}";
		//
		// 	return fullname;
		// }

		public static string BuildVolumeString(this VolumeDto volume)
		{
			if (volume == null)
			{
				return null;
			}

			var str = $" {volume.Name}";

			if (volume.Title.IsNotEmpty())
			{
				str += $" {volume.Title}";
			}

			return str;
		}

		public static string BuildVolumeString(this string volume, bool includeSpace = true)
		{
			if (string.IsNullOrEmpty(volume))
			{
				return null;
			}

			if (includeSpace)
			{
				volume = $"{volume}";
			}

			return volume;
		}

		public static string BuildReleaseDtString(this DateTime? releaseDt, bool includeBracket = true)
		{
			var str = releaseDt?.ToString("yyMMdd");
			if (!string.IsNullOrEmpty(str))
			{
				if (includeBracket)
				{
					str = $"[{str}]";
				}
			}

			return str;
		}

		#endregion

		#region Converter

		/// <summary>
		/// Be cautious, <see cref="Resource.Parent"/> will be cloned also.
		/// </summary>
		/// <param name="resource"></param>
		/// <returns></returns>


		public static ResourceProperty ToResourceProperty(this SearchableReservedProperty srp)
		{
			return (ResourceProperty) srp;
		}

		#endregion
	}
}