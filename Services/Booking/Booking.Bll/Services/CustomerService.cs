using AutoMapper;
using Booking.Bll.Dtos;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Booking.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Booking.Bll.Services;

public sealed class CustomerService(IUnitOfWork uow, IMapper mapper, ILogger<CustomerService> logger) : ICustomerService
{
    private const string UserName = "booking-api";

    public async Task<CustomerDto> GetAsync(long id, CancellationToken ct = default)
    {
        var customer = await uow.Customers.GetByIdAsync(id, ct)
                       ?? throw new NotFoundException($"Глядача {id} не знайдено.");
        return mapper.Map<CustomerDto>(customer);
    }

    public async Task<PagedResultDto<CustomerDto>> SearchAsync(string? term, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var result = await uow.Customers.SearchAsync(string.IsNullOrWhiteSpace(term) ? null : term.Trim(), page, pageSize, ct);
        return Paging.Map<Customer, CustomerDto>(result, mapper);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default)
    {
        // Унікальність email гарантує UNIQUE-індекс БД; дубль → BusinessConflictException (409) з DAL
        var id = await uow.Customers.AddAsync(new Customer
        {
            FullName = input.FullName.Trim(),
            Email = input.Email.Trim(),
            Phone = input.Phone
        }, UserName, ct);

        logger.LogInformation("Створено глядача {CustomerId}", id);
        return await GetAsync(id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default)
    {
        var updated = await uow.Customers.UpdateAsync(new Customer
        {
            Id = id,
            FullName = input.FullName.Trim(),
            Email = input.Email.Trim(),
            Phone = input.Phone,
            RowVersion = RowVersions.Decode(input.RowVersion)
        }, UserName, ct);

        if (!updated)
        {
            // Розрізняємо «немає такого» (404) і «змінив хтось інший» (409)
            _ = await uow.Customers.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Глядача {id} не знайдено.");
            throw new BusinessConflictException("Дані глядача вже змінено іншим користувачем. Отримайте актуальну версію й повторіть.");
        }

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        _ = await uow.Customers.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Глядача {id} не знайдено.");

        // Бізнес-правило, що потребує стану БД: глядача з активними бронюваннями видаляти не можна
        if (await uow.Bookings.HasActiveBookingsAsync(id, ct))
            throw new BusinessConflictException("У глядача є активні бронювання на майбутні сеанси — спершу скасуйте їх.");

        await uow.Customers.SoftDeleteAsync(id, UserName, ct);
        logger.LogInformation("Глядача {CustomerId} видалено (soft delete)", id);
    }
}
