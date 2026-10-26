using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People", t =>
            t.HasCheckConstraint("CK_People_BirthYear", $"[BirthYear] IS NULL OR [BirthYear] >= {Person.MinBirthYear}"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).IsRequired().HasMaxLength(Person.FullNameMaxLength);
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2(3)");
        builder.HasIndex(p => p.FullName);                                 // пошук людини за іменем

        builder.Navigation(p => p.Credits).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
