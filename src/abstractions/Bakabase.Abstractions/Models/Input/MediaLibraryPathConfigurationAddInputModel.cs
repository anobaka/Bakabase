using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input
{
	public record MediaLibraryPathConfigurationAddInputModel
	{
		[Required] public string Path { get; set; } = null!;
	}
}
