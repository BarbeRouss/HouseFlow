using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefonteUxMaintenanceAndInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BaselineDueDate",
                table: "MaintenanceTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomMonths",
                table: "MaintenanceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeclinedAt",
                table: "Invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Invitations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows written with the new values would be unreadable by the previous code (unknown
            // Periodicity int, Custom without CustomDays → ArgumentException, unknown "Declined"
            // status). Convert them to the closest previous representation before dropping columns:
            //  - Biennial (5)                   → Custom, CustomDays = 730
            //  - Custom with CustomMonths = n   → Custom, CustomDays = n * 30 (months take precedence)
            //  - Invitation status "Declined"   → "Revoked" (RevokedAt = DeclinedAt): both are final,
            //    non-usable states purged by the same retention rule.
            migrationBuilder.Sql(
                "UPDATE \"MaintenanceTypes\" SET \"Periodicity\" = 4, \"CustomDays\" = 730 WHERE \"Periodicity\" = 5;");
            migrationBuilder.Sql(
                "UPDATE \"MaintenanceTypes\" SET \"CustomDays\" = \"CustomMonths\" * 30 " +
                "WHERE \"Periodicity\" = 4 AND \"CustomMonths\" IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE \"Invitations\" SET \"Status\" = 'Revoked', \"RevokedAt\" = COALESCE(\"RevokedAt\", \"DeclinedAt\") " +
                "WHERE \"Status\" = 'Declined';");

            migrationBuilder.DropColumn(
                name: "BaselineDueDate",
                table: "MaintenanceTypes");

            migrationBuilder.DropColumn(
                name: "CustomMonths",
                table: "MaintenanceTypes");

            migrationBuilder.DropColumn(
                name: "DeclinedAt",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Invitations");
        }
    }
}
