using Catalog.Bll.Dtos;
using Catalog.Domain.Entities;
using FluentValidation;

namespace Catalog.Bll.Validators;

// Складні правила — у валідаторах FluentValidation (окремі класи, реєстрація AddValidatorsFromAssemblyContaining,
// явний виклик у контролерах). Прості Required / StringLength — DataAnnotations на DTO, тут не дублюються.

public class CreateMovieDtoValidator : AbstractValidator<CreateMovieDto>
{
    public CreateMovieDtoValidator()
    {
        RuleFor(x => x.ReleaseYear)
            .InclusiveBetween(Movie.MinReleaseYear, DateTime.UtcNow.Year + 5)
            .WithMessage($"Рік випуску має бути між {Movie.MinReleaseYear} і {DateTime.UtcNow.Year + 5}.");
        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(1, Movie.MaxDurationMinutes)
            .WithMessage($"Тривалість — від 1 до {Movie.MaxDurationMinutes} хв.");
        RuleFor(x => x.AgeRating)
            .Must(r => Movie.AgeRatings.Contains(r))
            .When(x => !string.IsNullOrEmpty(x.AgeRating))
            .WithMessage($"Віковий рейтинг: {string.Join(", ", Movie.AgeRatings)}.");
        RuleFor(x => x.Title)
            .Must(t => t.Trim().Length > 0).When(x => x.Title is not null)
            .WithMessage("Назва не може складатися лише з пробілів.");
    }
}

public sealed class UpdateMovieDtoValidator : AbstractValidator<UpdateMovieDto>
{
    public UpdateMovieDtoValidator()
    {
        Include(new CreateMovieDtoValidator());
        RuleFor(x => x.RowVersion)
            .Must(BeRowVersion).When(x => !string.IsNullOrEmpty(x.RowVersion))
            .WithMessage("RowVersion — 8 байт у base64, отримані з GET.");
    }

    internal static bool BeRowVersion(string value)
    {
        Span<byte> buffer = stackalloc byte[16];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 8;
    }
}

public sealed class SetMovieDetailDtoValidator : AbstractValidator<SetMovieDetailDto>
{
    public SetMovieDetailDtoValidator()
    {
        RuleFor(x => x.TrailerUrl).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.TrailerUrl))
            .WithMessage("Посилання має бути абсолютною http(s)-адресою.");
        RuleFor(x => x.PosterUrl).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.PosterUrl))
            .WithMessage("Посилання має бути абсолютною http(s)-адресою.");
        RuleFor(x => x.Budget).GreaterThan(0).When(x => x.Budget is not null)
            .WithMessage("Бюджет має бути додатним.");
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class AddCreditDtoValidator : AbstractValidator<AddCreditDto>
{
    public AddCreditDtoValidator()
    {
        RuleFor(x => x.Role)
            .Must(r => Enum.TryParse<CreditRole>(r, ignoreCase: true, out var v) && Enum.IsDefined(v))
            .When(x => !string.IsNullOrEmpty(x.Role))
            .WithMessage("Роль: Actor або Director.");

        When(x => string.Equals(x.Role, nameof(CreditRole.Actor), StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.CharacterName).NotEmpty().WithMessage("Для актора вкажіть ім'я персонажа.");
            RuleFor(x => x.BillingOrder).GreaterThanOrEqualTo(1).WithMessage("Порядок актора в титрах — від 1.");
        });
        When(x => string.Equals(x.Role, nameof(CreditRole.Director), StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.CharacterName).Empty().WithMessage("У режисера не може бути персонажа.");
            RuleFor(x => x.BillingOrder).Equal(0).WithMessage("Для режисера порядок у титрах — 0.");
        });
    }
}

public sealed class CreatePersonDtoValidator : AbstractValidator<CreatePersonDto>
{
    public CreatePersonDtoValidator()
    {
        RuleFor(x => x.BirthYear)
            .InclusiveBetween(Person.MinBirthYear, DateTime.UtcNow.Year).When(x => x.BirthYear is not null)
            .WithMessage($"Рік народження — між {Person.MinBirthYear} і поточним роком.");
    }
}

public sealed class MovieQueryValidator : AbstractValidator<MovieQuery>
{
    private static readonly string[] SortFields = ["title", "year", "duration"];

    public MovieQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Services.Paging.MaxPageSize);
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Сортування можливе лише за: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.SortDir).Must(BeSortDirection).WithMessage("Напрямок сортування: asc або desc.");
        RuleFor(x => x).Must(q => q.YearFrom is null || q.YearTo is null || q.YearFrom <= q.YearTo)
            .WithName("YearFrom").WithMessage("YearFrom не може бути більшим за YearTo.");
        RuleFor(x => x.AgeRating).Must(r => Movie.AgeRatings.Contains(r)).When(x => !string.IsNullOrEmpty(x.AgeRating))
            .WithMessage($"Віковий рейтинг: {string.Join(", ", Movie.AgeRatings)}.");
    }

    internal static bool BeSortDirection(string dir) =>
        string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) || string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
}

public sealed class PersonQueryValidator : AbstractValidator<PersonQuery>
{
    private static readonly string[] SortFields = ["name", "birthYear"];

    public PersonQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Services.Paging.MaxPageSize);
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Сортування можливе лише за: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.SortDir).Must(MovieQueryValidator.BeSortDirection).WithMessage("Напрямок сортування: asc або desc.");
        RuleFor(x => x).Must(q => q.BornFrom is null || q.BornTo is null || q.BornFrom <= q.BornTo)
            .WithName("BornFrom").WithMessage("BornFrom не може бути більшим за BornTo.");
    }
}
