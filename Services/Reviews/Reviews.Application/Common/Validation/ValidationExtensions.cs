using FluentValidation;
using MongoDB.Bson;
using Reviews.Domain.Enums;

namespace Reviews.Application.Common.Validation;

public static class ValidationExtensions
{
    /// <summary>MongoDB-специфічне правило: рядок має бути коректним ObjectId (24 hex-символи).</summary>
    public static IRuleBuilderOptions<T, string?> MustBeObjectId<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(id => ObjectId.TryParse(id, out var parsed) && parsed != ObjectId.Empty)
            .WithMessage("'{PropertyName}' має бути коректним ObjectId (24 шістнадцяткові символи).");

    /// <summary>Те саме для необов'язкового поля: null дозволено.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeObjectIdIfPresent<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(id => id is null || (ObjectId.TryParse(id, out var parsed) && parsed != ObjectId.Empty))
            .WithMessage("'{PropertyName}' має бути коректним ObjectId (24 шістнадцяткові символи).");

    public static IRuleBuilderOptions<T, string?> MustBeReviewKind<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(kind => ReviewKinds.TryParse(kind, out _))
            .WithMessage($"'{{PropertyName}}' має бути одним із: {string.Join(", ", ReviewKinds.Names)}.");
}

/// <summary>Назви видів відгуків у API та документах — як у Лаб. 01 (quick | full | critic).</summary>
public static class ReviewKinds
{
    public static readonly string[] Names = Enum.GetNames<ReviewKind>().Select(n => n.ToLowerInvariant()).ToArray();

    public static string ToName(ReviewKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out ReviewKind kind)
    {
        kind = default;
        return value is not null && Names.Contains(value) && Enum.TryParse(value, ignoreCase: true, out kind);
    }

    public static ReviewKind Parse(string value) =>
        TryParse(value, out var kind) ? kind : throw new ArgumentException($"Unknown review kind '{value}'", nameof(value));
}
