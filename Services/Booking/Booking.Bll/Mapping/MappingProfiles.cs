using AutoMapper;
using Booking.Bll.Dtos;
using Booking.Domain;

namespace Booking.Bll.Mapping;

// Усі профілі мапінгу зібрані тут, у BLL, і реєструються централізовано в Program.cs (AddMaps за збіркою BLL).
// Коректність конфігурації перевіряє тест MappingConfigurationTests (AssertConfigurationIsValid).

public sealed class CustomerProfile : Profile
{
    public CustomerProfile()
    {
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.RowVersion, o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));
    }
}

public sealed class SessionProfile : Profile
{
    public SessionProfile()
    {
        CreateMap<Session, SessionDto>()
            .ForMember(d => d.MovieTitle, o => o.MapFrom(s => s.Movie != null ? s.Movie.Title : ""))
            .ForMember(d => d.DurationMinutes, o => o.MapFrom(s => s.Movie != null ? s.Movie.DurationMinutes : 0))
            .ForMember(d => d.AgeRating, o => o.MapFrom(s => s.Movie != null ? s.Movie.AgeRating : ""))
            .ForMember(d => d.HallName, o => o.MapFrom(s => s.HallName ?? ""));

        CreateMap<SeatAvailability, SeatDto>();
    }
}

public sealed class BookingProfile : Profile
{
    public BookingProfile()
    {
        CreateMap<Ticket, TicketDto>();
        CreateMap<BookingPayment, PaymentDto>();
        CreateMap<Domain.Booking, BookingDto>()
            .ForMember(d => d.RowVersion, o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));
    }
}
