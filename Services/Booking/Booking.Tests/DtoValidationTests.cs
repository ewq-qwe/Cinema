using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Booking.Bll.Dtos;

namespace Booking.Tests;

/// <summary>
/// DataAnnotations на DTO мають працювати незалежно від культури ОС
/// (в uk-UA десятковий роздільник — кома, а межі Range задано як "0.01").
/// </summary>
public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("uk-UA")]
    [InlineData("en-US")]
    [InlineData("")]
    public void Pay_dto_amount_range_is_culture_independent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Empty(Validate(new PayBookingDto { Amount = 360.50m, Method = "card", TransactionId = "TXN-1" }));
            Assert.Contains(Validate(new PayBookingDto { Amount = 0m, Method = "card", TransactionId = "TXN-1" }),
                r => r.MemberNames.Contains(nameof(PayBookingDto.Amount)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Create_customer_dto_rejects_bad_email_and_short_name()
    {
        var errors = Validate(new CreateCustomerDto { FullName = "X", Email = "bad" });
        Assert.Contains(errors, r => r.MemberNames.Contains(nameof(CreateCustomerDto.Email)));
        Assert.Contains(errors, r => r.MemberNames.Contains(nameof(CreateCustomerDto.FullName)));
    }
}
