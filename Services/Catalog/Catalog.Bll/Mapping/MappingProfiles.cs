using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Dal.ReadModels;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Mapping;

// Профілі AutoMapper зібрані в BLL і реєструються централізовано в Program.cs (AddMaps за збіркою BLL).

public sealed class MovieProfile : Profile
{
    public MovieProfile()
    {
        CreateMap<Movie, MovieListItemDto>()
            .ForMember(d => d.GenreName, o => o.MapFrom(s => s.Genre.Name));

        CreateMap<Movie, MovieShortDto>();

        CreateMap<Movie, MovieDto>()
            .ForMember(d => d.GenreName, o => o.MapFrom(s => s.Genre.Name))
            .ForMember(d => d.Description, o => o.MapFrom(s => s.Detail != null ? s.Detail.Description : null))
            .ForMember(d => d.Country, o => o.MapFrom(s => s.Detail != null ? s.Detail.Country : null))
            .ForMember(d => d.TrailerUrl, o => o.MapFrom(s => s.Detail != null ? s.Detail.TrailerUrl : null))
            .ForMember(d => d.PosterUrl, o => o.MapFrom(s => s.Detail != null ? s.Detail.PosterUrl : null))
            .ForMember(d => d.Budget, o => o.MapFrom(s => s.Detail != null ? s.Detail.Budget : null))
            .ForMember(d => d.Credits, o => o.MapFrom(s => s.Credits.OrderBy(c => c.Role).ThenBy(c => c.BillingOrder)))
            .ForMember(d => d.RowVersion, o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));

        CreateMap<MovieCredit, CreditDto>()
            .ForMember(d => d.PersonFullName, o => o.MapFrom(s => s.Person.FullName))
            .ForMember(d => d.Role, o => o.MapFrom(s => s.Role.ToString()));
    }
}

public sealed class GenreProfile : Profile
{
    public GenreProfile()
    {
        CreateMap<Genre, GenreDto>();
        CreateMap<Genre, GenreWithMoviesDto>();
    }
}

public sealed class PersonProfile : Profile
{
    public PersonProfile()
    {
        CreateMap<Person, PersonDto>();
        CreateMap<FilmographyEntry, FilmographyItemDto>()
            .ForMember(d => d.Role, o => o.MapFrom(s => s.Role.ToString()));
    }
}
