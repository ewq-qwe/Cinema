using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Booking.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Booking.Tests;

public class CustomerServiceTests
{
    private readonly IUnitOfWork _uow = TestHelpers.CreateUow();
    private readonly CustomerService _service;

    public CustomerServiceTests() =>
        _service = new CustomerService(_uow, TestHelpers.CreateMapper(), NullLogger<CustomerService>.Instance);

    [Fact]
    public async Task Get_nonexistent_throws_NotFound()
    {
        _uow.Customers.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((Customer?)null);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_with_active_bookings_throws_Conflict_and_does_not_delete()
    {
        _uow.Customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _uow.Bookings.HasActiveBookingsAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.DeleteAsync(1, TestContext.Current.CancellationToken));
        await _uow.Customers.DidNotReceive().SoftDeleteAsync(Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_with_stale_row_version_throws_Conflict()
    {
        _uow.Customers.UpdateAsync(Arg.Any<Customer>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);
        _uow.Customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });

        var input = new UpdateCustomerDto { FullName = "Олена", Email = "o@example.com", RowVersion = "AAAAAAAAB9A=" };
        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.UpdateAsync(1, input, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_with_malformed_row_version_throws_Validation()
    {
        var input = new UpdateCustomerDto { FullName = "Олена", Email = "o@example.com", RowVersion = "not-base64" };
        await Assert.ThrowsAsync<ValidationException>(() => _service.UpdateAsync(1, input, TestContext.Current.CancellationToken));
    }
}
