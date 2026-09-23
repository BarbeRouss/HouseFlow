-- Vérifie qu'une base pseudonymisée par pseudonymize.sql ne contient plus aucune donnée
-- personnelle hors des comptes préservés. Une ligne par contrôle en échec (nom, nombre de
-- lignes fautives) ; aucune ligne = la base peut quitter la prod.
--
-- Même entrée que pseudonymize.sql (`dbtools.preserved_emails`), et des contrôles écrits
-- contre les valeurs attendues plutôt que contre les valeurs d'origine : ils attrapent aussi
-- une ligne que la pseudonymisation aurait manquée.

WITH preserved_users AS (
    SELECT "Id" FROM "Users"
    WHERE lower("Email") = ANY (string_to_array(
        lower(replace(current_setting('dbtools.preserved_emails', true), ' ', '')), ','))
),
preserved_houses AS (
    SELECT "Id" FROM "Houses" WHERE "UserId" IN (SELECT "Id" FROM preserved_users)
),
other_users AS (
    SELECT * FROM "Users" WHERE "Id" NOT IN (SELECT "Id" FROM preserved_users)
),
other_houses AS (
    SELECT * FROM "Houses" WHERE "Id" NOT IN (SELECT "Id" FROM preserved_houses)
),
checks (name, n) AS (
    SELECT 'Users.Email', count(*) FROM other_users
    WHERE "Email" !~ '^user-[0-9a-f]{32}@pseudonymise\.invalid$'
    UNION ALL
    SELECT 'Users.FirstName', count(*) FROM other_users WHERE "FirstName" <> 'Prénom'
    UNION ALL
    SELECT 'Users.LastName', count(*) FROM other_users WHERE "LastName" !~ '^Nom-[0-9a-f]{8}$'
    UNION ALL
    SELECT 'Users.PasswordHash', count(*) FROM other_users
    WHERE "PasswordHash" <> '$2a$11$' || repeat('A', 53)
    UNION ALL
    SELECT 'RefreshTokens', count(*) FROM "RefreshTokens"
    UNION ALL
    SELECT 'ApiKeys', count(*) FROM "ApiKeys"
    WHERE "UserId" NOT IN (SELECT "Id" FROM preserved_users)
    UNION ALL
    SELECT 'AuditLogs', count(*) FROM "AuditLogs"
    WHERE "Username" IS NOT NULL OR "IpAddress" IS NOT NULL OR "UserAgent" IS NOT NULL
       OR "OldValues" IS NOT NULL OR "NewValues" IS NOT NULL OR "AdditionalData" IS NOT NULL
    UNION ALL
    SELECT 'Invitations.Token', count(*) FROM "Invitations"
    WHERE "HouseId" NOT IN (SELECT "Id" FROM preserved_houses)
      AND "Token" !~ '^pseudo-[0-9a-f]{32}$'
    UNION ALL
    SELECT 'Houses.Name', count(*) FROM other_houses WHERE "Name" !~ '^Maison [0-9a-f]{8}$'
    UNION ALL
    SELECT 'Houses.Address', count(*) FROM other_houses
    WHERE "Address" IS DISTINCT FROM NULL AND "Address" <> 'Adresse pseudonymisée'
    UNION ALL
    SELECT 'Houses.ZipCode', count(*) FROM other_houses
    WHERE "ZipCode" IS DISTINCT FROM NULL AND "ZipCode" <> '0000'
    UNION ALL
    SELECT 'Houses.City', count(*) FROM other_houses
    WHERE "City" IS DISTINCT FROM NULL AND "City" <> 'Ville pseudonymisée'
    UNION ALL
    -- Toutes les instances, maisons préservées comprises : ces champs nomment un tiers.
    SELECT 'MaintenanceInstances.Provider', count(*) FROM "MaintenanceInstances"
    WHERE "Provider" IS DISTINCT FROM NULL AND "Provider" <> 'Prestataire pseudonymisé'
    UNION ALL
    SELECT 'MaintenanceInstances.Notes', count(*) FROM "MaintenanceInstances"
    WHERE "Notes" IS DISTINCT FROM NULL AND "Notes" <> 'Note pseudonymisée'
)
SELECT name, n FROM checks WHERE n > 0 ORDER BY name;
