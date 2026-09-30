using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Maps the RefreshToken concurrency token onto PostgreSQL's <c>xmin</c> system column. The
    /// operation only updates the EF model: Npgsql emits no SQL for a system column (checked with
    /// <c>dotnet ef migrations script</c> — the script only records the migration).
    /// </summary>
    public partial class AddRefreshTokenConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "RefreshTokens",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "RefreshTokens");
        }
    }
}
