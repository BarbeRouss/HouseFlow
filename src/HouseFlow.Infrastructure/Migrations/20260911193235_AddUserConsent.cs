using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentPolicyVersion",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ConsentPolicyVersion",
                table: "Users");
        }
    }
}
