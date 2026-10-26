using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal;

/// <summary>Контекст БД каталогу (Code-First). Схема — з конфігурацій у Configurations/ та міграцій у Migrations/.</summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieDetail> MovieDetails => Set<MovieDetail>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<MovieCredit> MovieCredits => Set<MovieCredit>();

    /// <summary>Усі IEntityTypeConfiguration&lt;T&gt; цієї збірки підключаються централізовано.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
}
