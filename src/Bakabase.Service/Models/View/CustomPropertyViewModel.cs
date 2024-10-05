using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.View;

public record CustomPropertyViewModel : PropertyViewModel
{
    public int? ValueCount { get; set; }
    public List<Category>? Categories { get; set; }
}