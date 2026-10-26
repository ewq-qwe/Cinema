using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string DefaultConnection =
        "Server=localhost,1433;Database=CinemaCatalog;User Id=sa;Password=Cinema_Dev_2026!;TrustServerCertificate=True";

    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieDetail> MovieDetails => Set<MovieDetail>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<MovieCredit> MovieCredits => Set<MovieCredit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------- Genre ----------
        b.Entity<Genre>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();                       // жанр з такою назвою — лише один
            ConfigureAudit(e);
        });

        // ---------- Movie ----------
        b.Entity<Movie>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Movies_Duration", "[DurationMinutes] BETWEEN 1 AND 600");
                t.HasCheckConstraint("CK_Movies_ReleaseYear", "[ReleaseYear] BETWEEN 1895 AND 2100");
                t.HasCheckConstraint("CK_Movies_AgeRating", "[AgeRating] IN ('0+', '6+', '12+', '16+', '18+')");
            });

            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.AgeRating).HasMaxLength(5).IsUnicode(false).HasDefaultValue("0+");
            e.Property(x => x.IsDeleted).HasDefaultValue(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            ConfigureAudit(e);

            // 1:N — жанр зі фільмами видалити не можна
            e.HasOne(x => x.Genre).WithMany(g => g.Movies)
             .HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Restrict);

            // Запит: пошук фільму за назвою (LIKE 'карп%')
            e.HasIndex(x => x.Title);
            // Запит: фільми жанру, новіші спочатку (лише не видалені)
            e.HasIndex(x => new { x.GenreId, x.ReleaseYear }).HasFilter("[IsDeleted] = 0");
            // Унікальність: назва + рік (ремейк того ж року неможливий)
            e.HasIndex(x => new { x.Title, x.ReleaseYear }).IsUnique();

            e.HasQueryFilter(x => !x.IsDeleted);                      // soft delete: видалені не потрапляють у запити
        });

        // ---------- MovieDetail: 1:1 ----------
        b.Entity<MovieDetail>(e =>
        {
            e.HasKey(x => x.MovieId);                                 // PK = FK
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Country).HasMaxLength(60).IsRequired();
            e.Property(x => x.TrailerUrl).HasMaxLength(300).IsUnicode(false);
            e.Property(x => x.PosterUrl).HasMaxLength(300).IsUnicode(false);

            e.HasOne(x => x.Movie).WithOne(m => m.Detail)
             .HasForeignKey<MovieDetail>(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => !x.Movie.IsDeleted);
        });

        // ---------- Person ----------
        b.Entity<Person>(e =>
        {
            e.ToTable("People", t => t.HasCheckConstraint("CK_People_BirthYear", "[BirthYear] IS NULL OR [BirthYear] BETWEEN 1850 AND 2025"));
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.FullName);                              // Запит: пошук актора за прізвищем
            ConfigureAudit(e);
        });

        // ---------- MovieCredit: M:N з власними полями ----------
        b.Entity<MovieCredit>(e =>
        {
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MovieCredits_Role", "[Role] IN ('Actor', 'Director')");
                t.HasCheckConstraint("CK_MovieCredits_Order", "[BillingOrder] >= 0");
            });

            // Складений ключ: одна людина — одна роль певного типу у фільмі
            e.HasKey(x => new { x.MovieId, x.PersonId, x.Role });
            e.Property(x => x.Role).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.CharacterName).HasMaxLength(100);

            e.HasOne(x => x.Movie).WithMany(m => m.Credits)
             .HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Person).WithMany(p => p.Credits)
             .HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);

            // Запит: фільмографія людини (FK PersonId індексується автоматично)
            e.HasQueryFilter(x => !x.Movie.IsDeleted);
        });

        CatalogSeed.Apply(b);
    }

    private static void ConfigureAudit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : AuditableEntity
    {
        e.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        e.Property(x => x.CreatedBy).HasMaxLength(100);
        e.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");
        e.Property(x => x.UpdatedBy).HasMaxLength(100);
    }

    /// <summary>Автоматично заповнює аудит і перетворює Remove() фільму на soft delete.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var user = Environment.UserName;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is Movie movie)
            {
                entry.State = EntityState.Modified;
                movie.IsDeleted = true;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = user;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = user;
            }
        }

        return base.SaveChangesAsync(ct);
    }
}

/// <summary>Потрібен інструменту dotnet ef (міграції).</summary>
public class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("CINEMA_CATALOG_DB") ?? CatalogDbContext.DefaultConnection)
            .Options);
}
