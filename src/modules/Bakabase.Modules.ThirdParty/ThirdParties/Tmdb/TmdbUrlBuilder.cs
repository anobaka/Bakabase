namespace Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;

public static class TmdbUrlBuilder
{
    public const string Domain = "https://api.themoviedb.org/3";
    public const string ImageBaseUrl = "https://image.tmdb.org/t/p/";
    
    public static string SearchMovie(string query) => $"{Domain}/search/movie?query={Uri.EscapeDataString(query)}";
    
    public static string MovieDetail(int movieId) => $"{Domain}/movie/{movieId}";
    
    public static string MovieImages(int movieId) => $"{Domain}/movie/{movieId}/images";
    
    public static string GetImageUrl(string imagePath, string size = "w500") => $"{ImageBaseUrl}{size}{imagePath}";
    
    public static string GetPosterUrl(string posterPath, string size = "w500") => GetImageUrl(posterPath, size);
    
    public static string GetBackdropUrl(string backdropPath, string size = "w1280") => GetImageUrl(backdropPath, size);
}