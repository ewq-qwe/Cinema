using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

/// <summary>Проміжна сутність M:N зі складеним ключем і власними полями.</summary>
public sealed class MovieCreditConfiguration : IEntityTypeConfiguration<MovieCredit>
{
    public void Configure(EntityTypeBuilder<MovieCredit> builder)
    {
        builder.ToTable("MovieCredits", t =>
        {
            t.HasCheckConstraint("CK_MovieCredits_Role", "[Role] IN ('Actor', 'Director')");
            t.HasCheckConstraint("CK_MovieCredits_BillingOrder", "[BillingOrder] >= 0");
        });

        // Складений ключ: людина може бути в одному фільмі і режисером, і актором, але кожною роллю — один раз
        builder.HasKey(c => new { c.MovieId, c.PersonId, c.Role });

        builder.Property(c => c.Role).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(c => c.CharacterName).HasMaxLength(MovieCredit.CharacterNameMaxLength);
        builder.Property(c => c.CreditedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(c => c.Person)
               .WithMany(p => p.Credits)
               .HasForeignKey(c => c.PersonId)
               .OnDelete(DeleteBehavior.Restrict);                          // людину з фільмографією не видалити

        // Фільмографія людини / головні ролі: фільтр за PersonId + Role, сортування за порядком у титрах
        builder.HasIndex(c => new { c.PersonId, c.Role, c.BillingOrder });

        builder.HasQueryFilter(c => !c.Movie.IsArchived);                  // узгоджено з фільтром Movie
    }
}
