using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HouseFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Aligns the stored emails on their canonical form (<c>EmailNormalizer</c>: trimmed, lower
    /// case), which the application now writes and looks up. Before, an address was stored as
    /// typed and login compared it exactly: « Jean@… » could not log in as « jean@… ».
    /// <para>
    /// <b>Case duplicates fail the migration on purpose.</b> Two accounts whose emails differ only
    /// by case (or surrounding spaces) cannot both become canonical under the unique index, and no
    /// automatic choice is safe: merging would hand one person's houses, history and password to
    /// the other, dropping one would destroy personal data. Registration and profile updates have
    /// compared emails case-insensitively for a long time, so this should never happen; if it does,
    /// the deployment stops here (the migrate init container fails, the running revision keeps
    /// serving) and an operator settles those accounts by hand first. The error only gives a count —
    /// never an address in logs (RGPD) — and the query that lists them.
    /// </para>
    /// <para>
    /// Invitation emails have no uniqueness constraint: they are simply lower-cased. Public for
    /// <c>EmailNormalizationMigrationTests</c>.
    /// </para>
    /// </summary>
    public partial class NormalizeEmailCase : Migration
    {
        public const string NormalizeSql =
            """
            DO $$
            DECLARE
                duplicates integer;
            BEGIN
                SELECT count(*) INTO duplicates
                FROM (
                    SELECT lower(btrim("Email"))
                    FROM "Users"
                    GROUP BY lower(btrim("Email"))
                    HAVING count(*) > 1
                ) AS d;

                IF duplicates > 0 THEN
                    RAISE EXCEPTION 'NormalizeEmailCase: % email address(es) are shared by several accounts once case and surrounding spaces are ignored', duplicates
                        USING HINT = 'Settle these accounts by hand before migrating: SELECT lower(btrim("Email")), count(*) FROM "Users" GROUP BY 1 HAVING count(*) > 1';
                END IF;
            END $$;

            UPDATE "Users"
            SET "Email" = lower(btrim("Email"))
            WHERE "Email" <> lower(btrim("Email"));

            UPDATE "Invitations"
            SET "Email" = lower(btrim("Email"))
            WHERE "Email" IS NOT NULL AND "Email" <> lower(btrim("Email"));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(NormalizeSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the original casing is not kept, and canonical emails remain valid
            // for the previous code (its uniqueness checks were already case-insensitive).
        }
    }
}
