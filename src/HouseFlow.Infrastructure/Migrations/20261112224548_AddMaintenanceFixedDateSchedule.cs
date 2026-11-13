using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceFixedDateSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnchorDay",
                table: "MaintenanceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnchorMonth",
                table: "MaintenanceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScheduleMode",
                table: "MaintenanceTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnchorDay",
                table: "MaintenanceTypes");

            migrationBuilder.DropColumn(
                name: "AnchorMonth",
                table: "MaintenanceTypes");

            migrationBuilder.DropColumn(
                name: "ScheduleMode",
                table: "MaintenanceTypes");
        }
    }
}
