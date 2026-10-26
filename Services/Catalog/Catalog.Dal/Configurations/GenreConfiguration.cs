using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(Genre.NameMaxLength);
        builder.HasIndex(g => g.Name).IsUnique();                      // жанр з такою назвою — один

        // Статичний (модельний) сідінг довідника з фіксованими Id — потрапляє в міграцію.
        // Setter-и приватні, тому дані задаються анонімними об'єктами.
        builder.HasData(
            new { Id = 1, Name = "Пригоди" },
            new { Id = 2, Name = "Детектив" },
            new { Id = 3, Name = "Анімація" },
            new { Id = 4, Name = "Драма" },
            new { Id = 5, Name = "Трилер" },
            new { Id = 6, Name = "Комедія" });
    }
}
