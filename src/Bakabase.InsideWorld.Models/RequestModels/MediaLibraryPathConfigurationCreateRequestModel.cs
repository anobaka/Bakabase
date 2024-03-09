using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
	public record MediaLibraryPathConfigurationCreateRequestModel
	{
		[Required] public string Path { get; set; } = null!;
	}
}
