using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Booking.Infrastructure.Migrations
{
    // Verification migration, not a real schema change. Generated via:
    //   dotnet ef migrations add TestEventIdSeatIdMapping --project Booking.Infrastructure --startup-project Booking.Infrastructure
    // Purpose: confirm EF Core could build the model after EventId/SeatId (record struct
    // Value Objects) replaced the raw string EventId/SeatId properties on Booking, once
    // BookingDbContext.OnModelCreating added .HasConversion(...) for both. Up/Down are
    // empty because the DB column stays nvarchar either way — only the C# side type
    // changed from string to a Value Object; the physical schema is untouched.
    /// <inheritdoc />
    public partial class TestEventIdSeatIdMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
