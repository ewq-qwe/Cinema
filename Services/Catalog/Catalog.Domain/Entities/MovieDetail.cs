using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>Розширений опис фільму. 1:1 з <see cref="Movie"/>: PK MovieId — водночас FK.</summary>
public class MovieDetail
{
    public const int DescriptionMaxLength = 2000;
    public const int CountryMaxLength = 60;
    public const int UrlMaxLength = 300;

    public int MovieId { get; private set; }
    public string Description { get; private set; } = "";
    public string Country { get; private set; } = "";
    public string? TrailerUrl { get; private set; }
    public string? PosterUrl { get; private set; }
    /// <summary>Бюджет фільму, USD (decimal(18,2)); необов'язковий.</summary>
    public decimal? Budget { get; private set; }

    private MovieDetail() { } // для EF Core

    internal MovieDetail(string description, string country, string? trailerUrl, string? posterUrl, decimal? budget) =>
        Update(description, country, trailerUrl, posterUrl, budget);

    internal void Update(string description, string country, string? trailerUrl, string? posterUrl, decimal? budget)
    {
        if (budget is <= 0)
            throw new DomainException("Бюджет має бути додатним.");
        description = description?.Trim() ?? "";
        country = country?.Trim() ?? "";
        if (description.Length is 0 or > DescriptionMaxLength)
            throw new DomainException($"Опис обов'язковий і не довший за {DescriptionMaxLength} символів.");
        if (country.Length is 0 or > CountryMaxLength)
            throw new DomainException($"Країна обов'язкова і не довша за {CountryMaxLength} символів.");
        if (!IsValidUrl(trailerUrl) || !IsValidUrl(posterUrl))
            throw new DomainException("Посилання на трейлер і постер мають бути абсолютними http(s)-адресами.");

        Description = description;
        Country = country;
        TrailerUrl = string.IsNullOrWhiteSpace(trailerUrl) ? null : trailerUrl;
        PosterUrl = string.IsNullOrWhiteSpace(posterUrl) ? null : posterUrl;
        Budget = budget;
    }

    private static bool IsValidUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ||
        (url.Length <= UrlMaxLength && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
