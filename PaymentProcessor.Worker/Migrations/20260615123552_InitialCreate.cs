using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentProcessor.Worker.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            // create table only if it doesn't exist
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TicketOrders' AND xtype='U')
                BEGIN
                    CREATE TABLE TicketOrders (
                        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                        SeatId NVARCHAR(MAX) NOT NULL,
                        UserId NVARCHAR(MAX) NOT NULL,
                        ProcessedAt DATETIME2 NOT NULL,
                        Status NVARCHAR(MAX) NOT NULL                        
                    )
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "TicketOrders");
        }
    }
}
