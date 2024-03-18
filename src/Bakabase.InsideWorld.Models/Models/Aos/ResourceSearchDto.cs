using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.RequestModels;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Aos
{
	public class ResourceSearchDto : SearchRequestModel
	{
		public string? Name { set; get; }
		public DateTime? ReleaseStartDt { set; get; }
		public DateTime? ReleaseEndDt { set; get; }
		public DateTime? AddStartDt { set; get; }
		public DateTime? AddEndDt { set; get; }
		public DateTime? FileCreateStartDt { get; set; }
		public DateTime? FileCreateEndDt { get; set; }
		public DateTime? FileModifyStartDt { get; set; }
		public DateTime? FileModifyEndDt { get; set; }

		public int[]? FavoritesIds { get; set; }
		public int? CategoryId { get; set; }
		public string? Publisher { get; set; }

		public string? Original { get; set; }
		public string? Series { get; set; }
		public decimal? MinRate { get; set; }
		public ResourceLanguage[]? Languages { set; get; }
		public int[]? MediaLibraryIds { get; set; }
		public int[]? TagIds { get; set; }
		public int[]? ExcludedTagIds { get; set; }
		public string? Everything { get; set; }
		public ResourceSearchOptions.OrderModel[]? Orders { get; set; }
		public Dictionary<string, string> CustomProperties { get; set; } = new();
		public List<string> CustomPropertyKeys { get; set; } = new();
		public int? ParentId { get; set; }
		public override int PageSize { get; set; } = 100;

		public bool HideChildren { get; set; }
		public bool Save { get; set; }
		public List<int>? CustomPropertyIds { get; set; }
		public ResourceSearchByCustomPropertyValuesRequestModel? CustomPropertiesV2 { get; set; }
	}
}