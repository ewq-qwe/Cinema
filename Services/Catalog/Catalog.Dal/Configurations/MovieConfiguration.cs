using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies", t =>
        {
            t.HasCheckConstraint("CK_Movies_Duration", $"[DurationMinutes] BETWEEN 1 AND {Movie.MaxDurationMinutes}");
            t.HasCheckConstraint("CK_Movies_ReleaseYear", $"[ReleaseYear] >= {Movie.MinReleaseYear}");
            t.HasCheckConstraint("CK_Movies_AgeRating", "[AgeRating] IN ('0+', '6+', '12+', '16+', '18+')");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(Movie.TitleMaxLength);
        builder.Property(m => m.AgeRating).IsRequired().HasMaxLength(5).IsUnicode(false);
        builder.Property(m => m.IsArchived).HasDefaultValue(false);
        builder.Property(m => m.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(m => m.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(m => m.RowVersion).IsRowVersion();              // токен оптимістичної конкурентності

        // Індекси під запити каталогу
        builder.HasIndex(m => m.Title);                                    // пошук за назвою
        builder.HasIndex(m => new { m.Title, m.ReleaseYear }).IsUnique();  // фільм = назва + рік
        builder.HasIndex(m => new { m.GenreId, m.ReleaseYear })            // фільми жанру, новіші спочатку
               .HasFilter("[IsArchived] = 0");

        // 1:N — жанр зі фільмами видалити не можна
        builder.HasOne(m => m.Genre)
               .WithMany(g => g.Movies)
               .HasForeignKey(m => m.GenreId)
               .OnDelete(DeleteBehavior.Restrict);

        // 1:1 — PK опису є водночас FK на фільм
        builder.HasOne(m => m.Detail)
               .WithOne()
               .HasForeignKey<MovieDetail>(d => d.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        // M:N через явну проміжну сутність (налаштування ключа — у MovieCreditConfiguration)
        builder.HasMany(m => m.Credits)
               .WithOne(c => c.Movie)
               .HasForeignKey(c => c.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Credits).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasQueryFilter(m => !m.IsArchived);                        // архівні фільми не потрапляють у запити
    }
}
