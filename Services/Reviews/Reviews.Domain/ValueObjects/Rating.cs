using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Оцінка фільму 1..5. Незмінна; некоректне значення неможливо навіть створити.
/// У документі зберігається як число <c>rating: 5</c> (власний серіалізатор в Infrastructure).
/// </summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    public Rating(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException($"Оцінка має бути від {Min} до {Max}.");
        Value = value;
    }

    public bool IsPositive => Value >= 4;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value}/{Max}";
}
