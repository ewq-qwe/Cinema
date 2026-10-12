using AutoMapper;
using Booking.Bll.Dtos;
using Booking.Domain;
using Booking.Domain.Exceptions;

namespace Booking.Bll.Services;

internal static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), pageSize is < 1 or > MaxPageSize ? 20 : pageSize);

    public static PagedResultDto<TDto> Map<T, TDto>(PagedResult<T> source, IMapper mapper) => new()
    {
        Items = mapper.Map<IReadOnlyList<TDto>>(source.Items),
        Page = source.Page,
        PageSize = source.PageSize,
        TotalCount = source.TotalCount
    };
}

internal static class RowVersions
{
    public static byte[] Decode(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }

        throw new ValidationException("Некоректне значення RowVersion.",
            new Dictionary<string, string[]> { ["rowVersion"] = ["Очікується 8 байт у base64, отримані з GET."] });
    }

    public static byte[]? DecodeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : Decode(value);
}
