using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenGraceUsedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GraceUsedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);

            // Les jetons émis avant le passage au hachage SHA-256 sont stockés EN CLAIR
            // (RGPD Art. 32(1)(a)). Ils ne sont de toute façon plus exploitables, le serveur
            // comparant désormais une empreinte : les conserver reviendrait à garder jusqu'à
            // 395 jours des secrets de session en clair, sauvegardes comprises, pour rien.
            // Effet de bord assumé : toutes les sessions en cours se terminent au déploiement.
            migrationBuilder.Sql("""DELETE FROM "RefreshTokens";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraceUsedAt",
                table: "RefreshTokens");
        }
    }
}
