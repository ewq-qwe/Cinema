using Catalog.Domain.Entities;

namespace Catalog.Dal.ReadModels;

/// <summary>Рядок фільмографії: проєкція Movie + власних полів проміжної сутності MovieCredit.</summary>
public sealed record FilmographyEntry(
    int MovieId,
    string Title,
    int ReleaseYear,
    string GenreName,
    CreditRole Role,
    string? CharacterName,
    int BillingOrder,
    DateTime CreditedAt);
