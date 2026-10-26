using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>Людина з команди фільмів (актор, режисер).</summary>
public class Person
{
    public const int FullNameMaxLength = 150;
    public const int MinBirthYear = 1850;

    private readonly List<MovieCredit> _credits = [];

    public int Id { get; private set; }
    public string FullName { get; private set; } = "";
    public int? BirthYear { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<MovieCredit> Credits => _credits;

    private Person() { } // для EF Core

    public Person(string fullName, int? birthYear)
    {
        Rename(fullName);
        SetBirthYear(birthYear);
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string fullName)
    {
        fullName = fullName?.Trim() ?? "";
        if (fullName.Length is < 2 or > FullNameMaxLength)
            throw new DomainException($"Ім'я має містити від 2 до {FullNameMaxLength} символів.");
        FullName = fullName;
    }

    public void SetBirthYear(int? birthYear)
    {
        if (birthYear is not null && (birthYear < MinBirthYear || birthYear > DateTime.UtcNow.Year))
            throw new DomainException($"Рік народження має бути між {MinBirthYear} і поточним роком.");
        BirthYear = birthYear;
    }
}
