using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Models.RequestModels;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Configs.Resource
{
	[Obsolete]
	public class ResourceSearchOptions : SearchRequestModel
	{
		public string Name { set; get; }
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

		/// <summary>
		/// 可能是Company、Individual、Circle，非精确搜索字段
		/// </summary>
		public string Publisher { get; set; }

		public string Original { get; set; }
		public decimal? MinRate { get; set; }
		public ResourceLanguage[] Languages { set; get; }
		public int[] MediaLibraryIds { get; set; }
		public int[] TagIds { get; set; }
		public int[]? ExcludedTagIds { get; set; }
		public string Everything { get; set; }

		/// <summary>
		/// Order - Asc, Asp.Net Core could not parse object array from query string.
		/// </summary>
		public OrderModel[] Orders { get; set; }

		public class OrderModel
		{
			public ResourceSearchOrder Order { get; set; }
			public bool Asc { get; set; }
		}

		public override int PageSize { get; set; } = 100;



		/// <summary>
		/// Colons can not exist in key of dictionary in Asp.Net Core Configuration, so we store the key using array and locate it by index.
		/// </summary>
		[Obsolete]
		public List<string> CustomPropertyKeys { get; set; } = new();

		[Obsolete] public List<string> CustomPropertyValues { get; set; } = new();
		public bool HideChildren { get; set; }

		public List<int>? CustomPropertyIds { get; set; }
		public ResourceSearchByCustomPropertyValuesRequestModel? CustomProperties { get; set; }
	}
}