using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Services
{
	public class SeriesService
	{
		private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Series, int> _orm;
		private readonly AliasService _aliasService;

		public SeriesService(FullMemoryCacheResourceService<InsideWorldDbContext, Series, int> orm,
			AliasService aliasService)
		{
			_orm = orm;
			_aliasService = aliasService;
		}

		public async Task<List<SeriesDto>> GetAll()
		{
			return (await _orm.GetAll()).Select(s => s.ToDto()!).ToList();
		}

		/// <summary>
		/// SerialId - Serial
		/// </summary>
		/// <param name="ids"></param>
		/// <returns></returns>
		public async Task<Dictionary<int, SeriesDto?>> GetByKeys(List<int> ids)
		{
			var serials = await _orm.GetByKeys(ids);
			var names = serials.Select(a => a.Name).Distinct().ToList();
			var aliases = await _aliasService.GetByNames(names, false, true);
			var serialsMap = serials.ToDictionary(a => a.Id, a => a);
			return ids.ToDictionary(a => a,
				a => serialsMap.TryGetValue(a, out var s) ? s.ToDto() : null);
		}

		/// <summary>
		/// todo: optimize
		/// </summary>
		/// <param name="names"></param>
		/// <param name="fuzzy"></param>
		/// <returns></returns>
		public async Task<List<int>> GetAllIdsByNames(HashSet<string> names, bool fuzzy)
		{
			Expression<Func<Series, bool>> exp =
				fuzzy ? a => names.Any(b => a.Name.Contains(b)) : a => names.Contains(a.Name);
			var serials = await _orm.GetAll(exp);
			return serials.Select(a => a.Id).ToList();
		}

		public async Task<string[]> GetNamesByIds(int[] ids)
		{
			var series = await _orm.GetByKeys(ids, false);
			return series.Select(t => t.Name).Distinct().ToArray();
		}

		/// <summary>
		/// If a record with the same name does not exist in the database, a new data entry will be created, regardless of whether it has an id.
		/// </summary>
		/// <param name="names"></param>
		/// <returns></returns>
		public async Task<SimpleRangeAddResult<SeriesDto>> GetOrAddRangeByNames(List<string> names)
		{
			names = names.Distinct().ToList();
			var existSeries = (await _orm.GetAll(a => names.Contains(a.Name))).ToList();
			var existSeriesNames = existSeries.Select(a => a.Name).ToList();
			var newSeries = names.Except(existSeriesNames).Select(a => new Series {Name = a}).ToList();
			var result = existSeries.Concat((await _orm.AddRange(newSeries)).Data)
				.ToDictionary(a => a.Name, a => a.ToDto());
			return new SimpleRangeAddResult<SeriesDto>
			{
				Data = result,
				AddedCount = newSeries.Count,
				ExistingCount = existSeries.Count
			};
		}
	}
}