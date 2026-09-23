-- Pseudonymise une COPIE de la base HouseFlow. `dbtools dump` l'applique à la base de travail
-- houseflow_dumpwork, jamais à houseflow_prod ; l'appelant l'exécute en une seule transaction.
--
-- Entrée : le paramètre de session `dbtools.preserved_emails`, liste d'e-mails séparés par des
-- virgules. Ces comptes, et les maisons qu'ils possèdent avec tout leur contenu, restent
-- intacts. Paramètre absent ou vide : rien n'est préservé.
--
-- Les valeurs de remplacement dérivent de l'Id : uniques, donc compatibles avec les index
-- uniques, et reconnaissables par verify.sql. Une colonne ajoutée au modèle doit être classée
-- ici ou déclarée non personnelle dans PseudonymizationTests — le test échoue sinon.

CREATE TEMP TABLE pseudo_preserved_users ON COMMIT DROP AS
SELECT "Id" FROM "Users"
WHERE lower("Email") = ANY (string_to_array(
    lower(replace(current_setting('dbtools.preserved_emails', true), ' ', '')), ','));

CREATE TEMP TABLE pseudo_preserved_houses ON COMMIT DROP AS
SELECT "Id" FROM "Houses"
WHERE "UserId" IN (SELECT "Id" FROM pseudo_preserved_users);

-- Des jetons de session de prod n'ont rien à faire ailleurs, préservés compris : les
-- environnements n'ont de toute façon pas la même clé JWT.
DELETE FROM "RefreshTokens";

-- Une clé d'API est un identifiant : on ne garde que celles des comptes préservés.
DELETE FROM "ApiKeys"
WHERE "UserId" NOT IN (SELECT "Id" FROM pseudo_preserved_users);

-- Hash au format BCrypt valide, qu'aucun mot de passe ne vérifie : la connexion échoue
-- proprement au lieu de lever une exception de format.
UPDATE "Users" SET
    "Email"        = 'user-' || replace("Id"::text, '-', '') || '@pseudonymise.invalid',
    "FirstName"    = 'Prénom',
    "LastName"     = 'Nom-' || left(replace("Id"::text, '-', ''), 8),
    "PasswordHash" = '$2a$11$' || repeat('A', 53)
WHERE "Id" NOT IN (SELECT "Id" FROM pseudo_preserved_users);

-- Les valeurs avant/après d'un audit sont du JSON libre, qui peut citer n'importe quel
-- champ de n'importe quel utilisateur : on les vide pour tout le monde. Restent l'action,
-- l'entité, la date et la liste des propriétés modifiées.
UPDATE "AuditLogs" SET
    "Username"       = NULL,
    "IpAddress"      = NULL,
    "UserAgent"      = NULL,
    "OldValues"      = NULL,
    "NewValues"      = NULL,
    "AdditionalData" = NULL;

UPDATE "Invitations" SET
    "Token" = 'pseudo-' || replace("Id"::text, '-', '')
WHERE "HouseId" NOT IN (SELECT "Id" FROM pseudo_preserved_houses);

UPDATE "Houses" SET
    "Name"    = 'Maison ' || left(replace("Id"::text, '-', ''), 8),
    "Address" = CASE WHEN "Address" IS NULL THEN NULL ELSE 'Adresse pseudonymisée' END,
    "ZipCode" = CASE WHEN "ZipCode" IS NULL THEN NULL ELSE '0000' END,
    "City"    = CASE WHEN "City" IS NULL THEN NULL ELSE 'Ville pseudonymisée' END
WHERE "Id" NOT IN (SELECT "Id" FROM pseudo_preserved_houses);

-- Texte libre : un prestataire peut être une personne, une note peut contenir n'importe quoi.
UPDATE "MaintenanceInstances" mi SET
    "Provider" = CASE WHEN mi."Provider" IS NULL THEN NULL ELSE 'Prestataire pseudonymisé' END,
    "Notes"    = CASE WHEN mi."Notes" IS NULL THEN NULL ELSE 'Note pseudonymisée' END
FROM "MaintenanceTypes" mt
JOIN "Devices" d ON d."Id" = mt."DeviceId"
WHERE mt."Id" = mi."MaintenanceTypeId"
  AND d."HouseId" NOT IN (SELECT "Id" FROM pseudo_preserved_houses);
