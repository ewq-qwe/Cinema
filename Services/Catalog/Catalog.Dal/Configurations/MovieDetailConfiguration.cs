using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class MovieDetailConfiguration : IEntityTypeConfiguration<MovieDetail>
{
    public void Configure(EntityTypeBuilder<MovieDetail> builder)
    {
        builder.ToTable("MovieDetails", t =>
            t.HasCheckConstraint("CK_MovieDetails_Budget", "[Budget] IS NULL OR [Budget] > 0"));

        builder.HasKey(d => d.MovieId);                                   // 1:1: PK = FK
        builder.Property(d => d.MovieId).ValueGeneratedNever();
        builder.Property(d => d.Description).IsRequired().HasMaxLength(MovieDetail.DescriptionMaxLength);
        builder.Property(d => d.Country).IsRequired().HasMaxLength(MovieDetail.CountryMaxLength);
        builder.Property(d => d.TrailerUrl).HasMaxLength(MovieDetail.UrlMaxLength).IsUnicode(false);
        builder.Property(d => d.PosterUrl).HasMaxLength(MovieDetail.UrlMaxLength).IsUnicode(false);
        builder.Property(d => d.Budget).HasColumnType("decimal(18,2)");
    }
}
