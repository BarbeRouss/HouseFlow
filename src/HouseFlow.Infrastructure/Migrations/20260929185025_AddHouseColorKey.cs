using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseColorKey : Migration
    {
        /// <summary>
        /// Backfills existing houses deterministically with the creation rotation (HouseColors.Next): per owner,
        /// in creation order (Id breaks ties), the n-th house gets the n-th palette colour modulo 6 — exactly
        /// what creating them one by one would have assigned. Public for HouseColorTests.
        /// </summary>
        public const string BackfillSql =
            """
            UPDATE "Houses" AS h
            SET "ColorKey" = (ARRAY['indigo','orange','green','sky','yellow','pink'])[((r.rn - 1) % 6) + 1]
            FROM (
                SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "UserId" ORDER BY "CreatedAt", "Id") AS rn
                FROM "Houses"
            ) AS r
            WHERE h."Id" = r."Id";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorKey",
                table: "Houses",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "indigo");

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorKey",
                table: "Houses");
        }
    }
}
