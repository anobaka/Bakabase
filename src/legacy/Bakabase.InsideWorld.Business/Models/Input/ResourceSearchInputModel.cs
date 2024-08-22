using Bakabase.Abstractions.Models.Dto;

namespace Bakabase.InsideWorld.Business.Models.Input
{
	public class ResourceSearchInputModel: ResourceSearchDto
    {
		public bool SaveSearchCriteria { get; set; }
	}
}
