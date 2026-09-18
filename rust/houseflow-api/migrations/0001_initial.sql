-- Schéma initial : reproduction exacte de la base migrée par EF Core (HouseFlow .NET).
-- Référence : pg_dump --schema-only de la base .NET, moins "__EFMigrationsHistory"
-- (remplacée ici par la table "_sqlx_migrations" de sqlx).
--
-- L'ordre des colonnes, les noms de contraintes (PK_*, IX_*, FK_*), les types, les
-- valeurs par défaut et les comportements ON DELETE sont identiques à EF : les deux
-- backends doivent pouvoir se brancher sur la même base.

CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Email" character varying(255) NOT NULL,
    "PasswordHash" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "FirstName" character varying(100) DEFAULT ''::character varying NOT NULL,
    "LastName" character varying(100) DEFAULT ''::character varying NOT NULL,
    "Language" character varying(10) DEFAULT 'fr'::character varying NOT NULL,
    "Theme" character varying(20) DEFAULT 'system'::character varying NOT NULL,
    "IsAdmin" boolean DEFAULT false NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

CREATE TABLE "Houses" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Address" character varying(500),
    "ZipCode" character varying(20),
    "City" character varying(200),
    "Country" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "UserId" uuid NOT NULL,
    CONSTRAINT "PK_Houses" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Houses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE TABLE "Devices" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Type" character varying(100) NOT NULL,
    "InstallDate" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "HouseId" uuid NOT NULL,
    "Brand" character varying(200),
    "Model" character varying(200),
    CONSTRAINT "PK_Devices" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Devices_Houses_HouseId" FOREIGN KEY ("HouseId") REFERENCES "Houses"("Id") ON DELETE CASCADE
);

CREATE TABLE "MaintenanceTypes" (
    "Id" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Periodicity" integer NOT NULL,
    "CustomDays" integer,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "DeviceId" uuid NOT NULL,
    CONSTRAINT "PK_MaintenanceTypes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MaintenanceTypes_Devices_DeviceId" FOREIGN KEY ("DeviceId") REFERENCES "Devices"("Id") ON DELETE CASCADE
);

CREATE TABLE "MaintenanceInstances" (
    "Id" uuid NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "Cost" numeric(18,2),
    "Provider" character varying(200),
    "Notes" character varying(2000),
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "MaintenanceTypeId" uuid NOT NULL,
    CONSTRAINT "PK_MaintenanceInstances" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MaintenanceInstances_MaintenanceTypes_MaintenanceTypeId" FOREIGN KEY ("MaintenanceTypeId") REFERENCES "MaintenanceTypes"("Id") ON DELETE CASCADE
);

CREATE TABLE "HouseMembers" (
    "Id" uuid NOT NULL,
    "Role" character varying(20) NOT NULL,
    "CanLogMaintenance" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    "UserId" uuid NOT NULL,
    "HouseId" uuid NOT NULL,
    "CanViewCosts" boolean DEFAULT false NOT NULL,
    CONSTRAINT "PK_HouseMembers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_HouseMembers_Houses_HouseId" FOREIGN KEY ("HouseId") REFERENCES "Houses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_HouseMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE TABLE "Invitations" (
    "Id" uuid NOT NULL,
    "Token" character varying(100) NOT NULL,
    "Role" character varying(20) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "AcceptedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "HouseId" uuid NOT NULL,
    "CreatedByUserId" uuid NOT NULL,
    "AcceptedByUserId" uuid,
    CONSTRAINT "PK_Invitations" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Invitations_Houses_HouseId" FOREIGN KEY ("HouseId") REFERENCES "Houses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Invitations_Users_AcceptedByUserId" FOREIGN KEY ("AcceptedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Invitations_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE TABLE "RefreshTokens" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedByIp" text,
    "RevokedAt" timestamp with time zone,
    "RevokedByIp" text,
    "ReplacedByToken" text,
    "ReasonRevoked" text,
    "FamilyId" uuid DEFAULT '00000000-0000-0000-0000-000000000000'::uuid NOT NULL,
    "RememberMe" boolean DEFAULT false NOT NULL,
    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE TABLE "ApiKeys" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Prefix" character varying(15) NOT NULL,
    "KeyHash" character varying(64) NOT NULL,
    "Scope" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedByIp" text,
    "LastUsedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_ApiKeys" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ApiKeys_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE TABLE "AuditLogs" (
    "Id" uuid NOT NULL,
    "EntityType" character varying(255) NOT NULL,
    "EntityId" character varying(255) NOT NULL,
    "Action" character varying(50) NOT NULL,
    "UserId" uuid,
    "Username" text,
    "Timestamp" timestamp with time zone NOT NULL,
    "OldValues" text,
    "NewValues" text,
    "ChangedProperties" text,
    "IpAddress" text,
    "UserAgent" text,
    "AdditionalData" text,
    CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_ApiKeys_Prefix" ON "ApiKeys" USING btree ("Prefix");
CREATE INDEX "IX_ApiKeys_UserId" ON "ApiKeys" USING btree ("UserId");
CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" USING btree ("EntityType", "EntityId");
CREATE INDEX "IX_AuditLogs_Timestamp" ON "AuditLogs" USING btree ("Timestamp");
CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" USING btree ("UserId");
CREATE INDEX "IX_Devices_HouseId" ON "Devices" USING btree ("HouseId");
CREATE INDEX "IX_HouseMembers_HouseId" ON "HouseMembers" USING btree ("HouseId");
CREATE UNIQUE INDEX "IX_HouseMembers_UserId_HouseId" ON "HouseMembers" USING btree ("UserId", "HouseId");
CREATE INDEX "IX_Houses_UserId" ON "Houses" USING btree ("UserId");
CREATE INDEX "IX_Invitations_AcceptedByUserId" ON "Invitations" USING btree ("AcceptedByUserId");
CREATE INDEX "IX_Invitations_CreatedByUserId" ON "Invitations" USING btree ("CreatedByUserId");
CREATE INDEX "IX_Invitations_HouseId" ON "Invitations" USING btree ("HouseId");
CREATE UNIQUE INDEX "IX_Invitations_Token" ON "Invitations" USING btree ("Token");
CREATE INDEX "IX_MaintenanceInstances_Date" ON "MaintenanceInstances" USING btree ("Date");
CREATE INDEX "IX_MaintenanceInstances_MaintenanceTypeId" ON "MaintenanceInstances" USING btree ("MaintenanceTypeId");
CREATE INDEX "IX_MaintenanceTypes_DeviceId" ON "MaintenanceTypes" USING btree ("DeviceId");
CREATE INDEX "IX_RefreshTokens_FamilyId" ON "RefreshTokens" USING btree ("FamilyId");
CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" USING btree ("Token");
CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" USING btree ("UserId");
CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" USING btree ("Email");
