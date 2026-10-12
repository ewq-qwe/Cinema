using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BirthYear = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.CheckConstraint("CK_People_BirthYear", "[BirthYear] IS NULL OR [BirthYear] BETWEEN 1850 AND 2025");
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GenreId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReleaseYear = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    AgeRating = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false, defaultValue: "0+"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.CheckConstraint("CK_Movies_AgeRating", "[AgeRating] IN ('0+', '6+', '12+', '16+', '18+')");
                    table.CheckConstraint("CK_Movies_Duration", "[DurationMinutes] BETWEEN 1 AND 600");
                    table.CheckConstraint("CK_Movies_ReleaseYear", "[ReleaseYear] BETWEEN 1895 AND 2100");
                    table.ForeignKey(
                        name: "FK_Movies_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovieCredits",
                columns: table => new
                {
                    MovieId = table.Column<long>(type: "bigint", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CharacterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieCredits", x => new { x.MovieId, x.PersonId, x.Role });
                    table.CheckConstraint("CK_MovieCredits_Order", "[BillingOrder] >= 0");
                    table.CheckConstraint("CK_MovieCredits_Role", "[Role] IN ('Actor', 'Director')");
                    table.ForeignKey(
                        name: "FK_MovieCredits_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieCredits_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovieDetails",
                columns: table => new
                {
                    MovieId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TrailerUrl = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true),
                    PosterUrl = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieDetails", x => x.MovieId);
                    table.ForeignKey(
                        name: "FK_MovieDetails_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Пригоди", null, null },
                    { 2, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Детектив", null, null },
                    { 3, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Анімація", null, null },
                    { 4, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Драма", null, null },
                    { 5, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Трилер", null, null },
                    { 6, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Комедія", null, null }
                });

            migrationBuilder.InsertData(
                table: "People",
                columns: new[] { "Id", "BirthYear", "CreatedAt", "CreatedBy", "FullName", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1, 1978, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Андрій Гнатюк", null, null },
                    { 2, 1995, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Марта Савчук", null, null },
                    { 3, 1988, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Олег Кравець", null, null },
                    { 4, 2001, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Софія Лисенко", null, null },
                    { 5, 1970, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Богдан Левицький", null, null },
                    { 6, 1992, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Ярина Пасічник", null, null },
                    { 7, 1983, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Дмитро Онищенко", null, null },
                    { 8, 1990, new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Наталія Руденко", null, null }
                });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "AgeRating", "CreatedAt", "CreatedBy", "DurationMinutes", "GenreId", "IsDeleted", "ReleaseYear", "Title", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { 1L, "12+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 118, 1, false, 2025, "Карпатський експрес", null, null },
                    { 2L, "12+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 104, 2, false, 2024, "Таємниця старого маяка", null, null },
                    { 3L, "0+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 92, 3, false, 2025, "Мандрівка до зірок", null, null },
                    { 4L, "16+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 131, 4, false, 2023, "Остання зміна", null, null },
                    { 5L, "16+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 109, 5, false, 2024, "Місто без світла", null, null },
                    { 6L, "6+", new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", 88, 6, false, 2025, "Кіт на ім'я Борщ", null, null }
                });

            migrationBuilder.InsertData(
                table: "MovieCredits",
                columns: new[] { "MovieId", "PersonId", "Role", "BillingOrder", "CharacterName" },
                values: new object[,]
                {
                    { 1L, 1, "Director", 0, null },
                    { 1L, 2, "Actor", 2, "Зоряна" },
                    { 1L, 3, "Actor", 1, "Мирон" },
                    { 2L, 3, "Actor", 2, "доглядач маяка" },
                    { 2L, 5, "Director", 0, null },
                    { 2L, 6, "Actor", 1, "слідча Ганна Ровенко" },
                    { 3L, 4, "Actor", 1, "Тимко (голос)" },
                    { 3L, 7, "Director", 0, null },
                    { 4L, 2, "Actor", 2, "медсестра Оля" },
                    { 4L, 5, "Director", 0, null },
                    { 4L, 8, "Actor", 1, "лікарка Віра" },
                    { 5L, 1, "Director", 0, null },
                    { 5L, 3, "Actor", 1, "детектив Шевчук" },
                    { 5L, 6, "Actor", 2, "журналістка" },
                    { 6L, 4, "Actor", 1, "ведуча шоу" },
                    { 6L, 7, "Director", 0, null }
                });

            migrationBuilder.InsertData(
                table: "MovieDetails",
                columns: new[] { "MovieId", "Country", "Description", "PosterUrl", "TrailerUrl" },
                values: new object[,]
                {
                    { 1L, "Україна", "Потяг через гори, зниклий вагон із золотом і троє незнайомців, яким доведеться довіритися одне одному.", "https://cdn.cinema-kadr.example/posters/1.jpg", null },
                    { 2L, "Україна", "Молода слідча приїжджає в приморське містечко, де щоночі на покинутому маяку загоряється світло.", "https://cdn.cinema-kadr.example/posters/2.jpg", null },
                    { 3L, "Україна", "Хлопчик і його робот-пес будують ракету з картонних коробок — і вона справді злітає.", "https://cdn.cinema-kadr.example/posters/3.jpg", null },
                    { 4L, "Україна, Польща", "Одна нічна зміна в лікарні маленького міста, що змінює життя п'ятьох людей.", "https://cdn.cinema-kadr.example/posters/4.jpg", null },
                    { 5L, "Україна", "Місто занурюється в темряву, і хтось використовує це, щоб приховати злочин.", "https://cdn.cinema-kadr.example/posters/5.jpg", null },
                    { 6L, "Україна", "Рудий кіт випадково стає зіркою кулінарного шоу.", "https://cdn.cinema-kadr.example/posters/6.jpg", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieCredits_PersonId",
                table: "MovieCredits",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_GenreId_ReleaseYear",
                table: "Movies",
                columns: new[] { "GenreId", "ReleaseYear" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Title",
                table: "Movies",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Title_ReleaseYear",
                table: "Movies",
                columns: new[] { "Title", "ReleaseYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_FullName",
                table: "People",
                column: "FullName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieCredits");

            migrationBuilder.DropTable(
                name: "MovieDetails");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Genres");
        }
    }
}
