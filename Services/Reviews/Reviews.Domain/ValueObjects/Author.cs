using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Автор відгуку чи повідомлення — вкладений документ <c>author { customerId, displayName }</c>.
/// CustomerId = Customers.Id у CinemaBooking (посилання між сервісами без FK).
/// </summary>
public sealed class Author : ValueObject
{
    public const int DisplayNameMaxLength = 60;

    public int CustomerId { get; }
    public string DisplayName { get; }

    public Author(int customerId, string displayName)
    {
        if (customerId <= 0)
            throw new DomainException("Некоректний ідентифікатор глядача.");
        displayName = displayName?.Trim() ?? "";
        if (displayName.Length is 0 or > DisplayNameMaxLength)
            throw new DomainException($"Ім'я автора обов'язкове і не довше за {DisplayNameMaxLength} символів.");

        CustomerId = customerId;
        DisplayName = displayName;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return DisplayName;
    }

    public override string ToString() => $"{DisplayName} (#{CustomerId})";
}
