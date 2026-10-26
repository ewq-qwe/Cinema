using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>Жанр — довідник. 1:N з <see cref="Movie"/>.</summary>
public class Genre
{
    public const int NameMaxLength = 50;

    private readonly List<Movie> _movies = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = "";

    public IReadOnlyCollection<Movie> Movies => _movies;

    private Genre() { } // для EF Core

    public Genre(string name) => Rename(name);

    public void Rename(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 2 or > NameMaxLength)
            throw new DomainException($"Назва жанру має містити від 2 до {NameMaxLength} символів.");
        Name = name;
    }
}
