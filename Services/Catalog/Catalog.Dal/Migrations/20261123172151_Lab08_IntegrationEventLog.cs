using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Dal.Migrations
{
    /// <inheritdoc />
    public partial class Lab08_IntegrationEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationEventLog",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Exchange = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RoutingKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    TraceParent = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    TimesSent = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEventLog", x => x.EventId);
                    table.CheckConstraint("CK_IntegrationEventLog_State", "[State] IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEventLog_Pending",
                table: "IntegrationEventLog",
                columns: new[] { "State", "OccurredAt" },
                filter: "[State] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationEventLog");
        }
    }
}
