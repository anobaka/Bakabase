using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Domain;

public record Alias
{
    public string Text { get; set; } = null!;
    public string Preferred { get; set; } = null!;
}