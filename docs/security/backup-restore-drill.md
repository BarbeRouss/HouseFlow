# Test de restauration de sauvegarde — mode opératoire

**Articles 32(1)(c) et 32(1)(d) du RGPD — rétablir la disponibilité, et en tester l'efficacité**

| Élément | Valeur |
|---|---|
| **Périodicité** | **Annuelle**, et après tout changement de version majeure de PostgreSQL, de SKU ou de topologie réseau |
| **Durée** | Une heure à une heure trente, dont ~20 minutes d'attente pendant la restauration |
| **Opérateur** | Le référent vie privée, ou tout contributeur disposant du rôle *Contributor* sur `rg-houseflow-prod` |
| **Ce qui est testé** | La **restauration réelle** d'une sauvegarde de production sur un serveur temporaire, pas la simple existence des sauvegardes |
| **Dernier test réalisé** | *[à compléter — voir § 7]* |
| **Prochain test prévu** | *[à compléter]* |

> Une sauvegarde n'existe pas tant qu'elle n'a pas été restaurée. L'Art. 32(1)(c) impose le moyen de rétablir la disponibilité ; l'Art. 32(1)(d) impose d'en **éprouver l'efficacité**. Le présent document est la procédure de cette épreuve, et le § 7 en est la preuve.

---

## 1. Ce que le test doit établir

Quatre affirmations, et rien d'autre. Un test qui n'en démontre pas les quatre a échoué, même si la restauration a « marché ».

| # | Affirmation à démontrer | Comment |
|---|---|---|
| 1 | **Une sauvegarde restaurable existe**, et la fenêtre annoncée (7 jours) est réelle | § 4, étape 1 — relevé de `earliestRestoreDate` |
| 2 | **La restauration aboutit** sur un serveur exploitable, sans intervention manuelle non documentée | § 4, étapes 2 à 4 |
| 3 | **Les données restaurées sont intègres et complètes** — schéma, migrations, volumétrie, clés étrangères | § 4, étape 5 |
| 4 | **La copie restaurée est détruite**, et sa destruction vérifiée | § 4, étape 7 |

---

## 2. Le test est lui-même un traitement de données personnelles

Le serveur restauré contient **toutes les données réelles de production**, en clair. La copie est donc un traitement à part entière, et les règles suivantes ne sont pas des précautions de confort.

- **Durée maximale d'existence de la copie : 4 heures.** Au-delà, elle est détruite même si le test n'est pas terminé ; le test est repris à une autre date.
- **Aucune donnée ne quitte Azure.** Pas de `pg_dump` vers un poste de travail, pas de copie de lignes dans un ticket, un presse-papier partagé ou une conversation. Les vérifications du § 5 sont conçues pour ne renvoyer que des **agrégats** — des comptes, des dates, des sommes de contrôle — jamais une adresse e-mail ni un nom.
- **Le serveur restauré n'est joignable que par le réseau privé**, comme la source : `public_network_access_enabled = false`, accès par le tunnel du bastion uniquement.
- **Aucune application n'est branchée dessus.** La copie ne reçoit ni le conteneur d'API, ni le job de rétention, ni `dbtools`.
- **La copie n'est pas pseudonymisée** — et c'est assumé : pseudonymiser interdirait de vérifier l'intégrité. C'est précisément pourquoi les quatre règles ci-dessus s'appliquent.
- **Le test est consigné** au § 7 : un traitement non documenté contredirait l'Art. 5(2).

---

## 3. Prérequis

À réunir **avant** d'ouvrir la fenêtre de test, faute de quoi l'heure prévue est passée à chercher une clé SSH.

- [ ] `az` CLI connectée sur l'abonnement de production (`az login`, puis `az account show`).
- [ ] Rôle **Contributor** sur `rg-houseflow-prod` (création et suppression d'un serveur et d'un subnet).
- [ ] Être **administrateur Entra du serveur** — c'est `var.entra_admin_object_id` / `var.entra_admin_name` de l'instance `prod` : l'authentification par mot de passe est désactivée (`password_auth_enabled = false`), il n'existe aucun mot de passe de secours.
- [ ] La **clé privée SSH** correspondant à `var.bastion_ssh_public_key`, le bastion étant le seul chemin vers le réseau privé.
- [ ] `psql` disponible localement (client uniquement).
- [ ] Une plage de deux heures sans déploiement en cours : la restauration lit les sauvegardes de la source, un `terraform apply` simultané brouillerait le relevé.

**Repères de la production** (source : `infrastructure/terraform/environment/`, instance `prod.tfvars`) :

| Élément | Valeur |
|---|---|
| Groupe de ressources | `rg-houseflow-prod` |
| Serveur source | `psql-houseflow-prod` |
| Base applicative | `houseflow_prod` |
| Rétention des sauvegardes | **7 jours**, `geo_redundant_backup_enabled = false` |
| VNet / subnet délégué | `vnet-houseflow-prod` / `snet-db` — `10.0.0.0/28` |
| Zone DNS privée | `houseflow-prod.private.postgres.database.azure.com` |
| Bastion | `ca-bastion-prod`, SSH sur le port `2222`, utilisateur `bastion` |

---

## 4. Déroulé

Les commandes sont à exécuter dans l'ordre. Chaque étape produit une **valeur à recopier** dans la fiche du § 7 : le test se prouve par ces valeurs, pas par le souvenir de l'avoir fait.

### Étape 1 — Relever l'état de la source

```bash
RG=rg-houseflow-prod
SRC=psql-houseflow-prod

az postgres flexible-server show -g "$RG" -n "$SRC" \
  --query "{version:version, sku:sku.name, storageGb:storage.storageSizeGb, \
            retentionDays:backup.backupRetentionDays, geoRedundant:backup.geoRedundantBackup, \
            earliestRestore:backup.earliestRestoreDate, state:state}" -o yaml
```

**À consigner** : `earliestRestore` et `retentionDays`. Si `earliestRestore` est postérieure à « maintenant moins 7 jours », la fenêtre réelle est **plus courte** que la fenêtre annoncée — c'est un écart à porter dans les constats, et à corriger dans la [politique de conservation](../gdpr/data-retention-policy.md#3-sauvegardes).

### Étape 2 — Choisir et figer l'instant de restauration

```bash
# Deux heures en arrière : assez récent pour être comparable à la production,
# assez ancien pour prouver qu'on remonte dans le temps et non qu'on copie l'instant présent.
RESTORE_TIME=$(date -u -d '2 hours ago' +%Y-%m-%dT%H:%M:%SZ)
echo "$RESTORE_TIME"
```

**À consigner** : `RESTORE_TIME`. Il devra être postérieur à `earliestRestore`.

### Étape 3 — Créer le subnet de restauration

Le subnet `snet-db` est un **/28**, le minimum exigé par Flexible Server : il n'a pas la place d'accueillir un second serveur. La restauration a donc besoin de son propre subnet délégué, créé pour l'occasion et détruit avec la copie. Il n'est volontairement pas décrit dans Terraform : il n'existe que quelques heures par an.

```bash
az network vnet subnet create \
  -g "$RG" --vnet-name vnet-houseflow-prod --name snet-db-restore \
  --address-prefixes 10.0.1.0/28 \
  --delegations Microsoft.DBforPostgreSQL/flexibleServers
```

`10.0.1.0/28` est libre dans `10.0.0.0/16` : `snet-db` occupe `10.0.0.0/28` et `snet-cae` `10.0.2.0/23`. Vérifier qu'aucun autre subnet n'a été ajouté depuis :

```bash
az network vnet subnet list -g "$RG" --vnet-name vnet-houseflow-prod \
  --query "[].{name:name, prefix:addressPrefix}" -o table
```

### Étape 4 — Restaurer (PITR)

```bash
DST="psql-houseflow-restoretest-$(date -u +%Y%m%d)"

az postgres flexible-server restore \
  -g "$RG" --name "$DST" \
  --source-server "$SRC" \
  --restore-time "$RESTORE_TIME" \
  --subnet "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RG/providers/Microsoft.Network/virtualNetworks/vnet-houseflow-prod/subnets/snet-db-restore" \
  --private-dns-zone "houseflow-prod.private.postgres.database.azure.com"
```

Compter **15 à 30 minutes** sur le SKU `B_Standard_B1ms`. **À consigner** : l'heure de lancement, l'heure de disponibilité, et le fait que la commande ait abouti sans étape manuelle.

> Si Azure refuse la réutilisation de la zone DNS privée, en créer une jetable (`az network private-dns zone create`) liée au même VNet, et le noter comme écart : cela signifie que la procédure de restauration réelle comporte une étape de plus que ce que la politique décrit.

### Étape 5 — Se connecter à la copie

```bash
DST_FQDN=$(az postgres flexible-server show -g "$RG" -n "$DST" --query fullyQualifiedDomainName -o tsv)
BASTION=$(az containerapp show -g "$RG" -n ca-bastion-prod --query properties.configuration.ingress.fqdn -o tsv)

# Tunnel sur un port local distinct de 5432, pour ne pas masquer une base locale
ssh -i <clé-privée> -N -L 15432:"$DST_FQDN":5432 bastion@"$BASTION" -p 2222 &

# Jeton Entra — valable une heure, à régénérer au-delà
export PGPASSWORD=$(az account get-access-token \
  --resource https://ossrdbms-aad.database.windows.net \
  --query accessToken -o tsv)

psql "host=localhost port=15432 dbname=houseflow_prod user=<UPN administrateur Entra> sslmode=require"
```

Le bastion est un Container App *scale-to-zero* : la première connexion peut échouer le temps du démarrage à froid. Réessayer une fois avant de conclure à une panne.

### Étape 6 — Vérifier l'intégrité

Les cinq requêtes ci-dessous ne renvoient que des agrégats (§ 2). Les exécuter **aussi sur la production** et comparer : c'est l'écart qui a du sens, pas la valeur absolue.

```sql
-- 6.1 Le schéma est complet : 11 tables applicatives attendues
--     (le schéma `hangfire` est séparé et hors périmètre ; `__EFMigrationsHistory`
--      est exclue, elle est vérifiée par la requête suivante)
SELECT count(*) AS tables_applicatives
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
  AND table_name NOT LIKE '\_\_%';

-- 6.2 Les migrations sont toutes appliquées : la dernière doit être celle de la production
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 3;

-- 6.3 Volumétrie par table — à comparer à la production
SELECT 'Users' t, count(*) n FROM "Users"
UNION ALL SELECT 'Houses',               count(*) FROM "Houses"
UNION ALL SELECT 'HouseMembers',         count(*) FROM "HouseMembers"
UNION ALL SELECT 'Devices',              count(*) FROM "Devices"
UNION ALL SELECT 'MaintenanceTypes',     count(*) FROM "MaintenanceTypes"
UNION ALL SELECT 'MaintenanceInstances', count(*) FROM "MaintenanceInstances"
UNION ALL SELECT 'Invitations',          count(*) FROM "Invitations"
UNION ALL SELECT 'RefreshTokens',        count(*) FROM "RefreshTokens"
UNION ALL SELECT 'ApiKeys',              count(*) FROM "ApiKeys"
UNION ALL SELECT 'AuditLogs',            count(*) FROM "AuditLogs"
UNION ALL SELECT 'Organizations',        count(*) FROM "Organizations"
ORDER BY t;

-- 6.4 Fraîcheur : la donnée la plus récente doit être antérieure à RESTORE_TIME,
--     et postérieure de peu — c'est ce qui prouve une restauration à l'instant demandé
SELECT max("Timestamp") AS dernier_evenement_audite FROM "AuditLogs";

-- 6.5 Intégrité référentielle : aucune contrainte de clé étrangère invalide
SELECT count(*) AS fk_non_validees
FROM pg_constraint WHERE contype = 'f' AND NOT convalidated;
```

**Critères de réussite** : 6.1 renvoie **11** ; 6.2 renvoie la même migration de tête que la production ; 6.3 ne s'écarte de la production que du volume créé depuis `RESTORE_TIME` ; 6.4 est antérieur à `RESTORE_TIME` de moins de quelques minutes ; 6.5 renvoie **0**.

### Étape 7 — Détruire la copie, et le vérifier

```bash
az postgres flexible-server delete -g "$RG" -n "$DST" --yes
az network vnet subnet delete -g "$RG" --vnet-name vnet-houseflow-prod --name snet-db-restore

# Vérification — la première commande doit échouer en ResourceNotFound
az postgres flexible-server show -g "$RG" -n "$DST" -o none 2>&1 | tail -1
az postgres flexible-server list -g "$RG" --query "[].name" -o tsv
```

**À consigner** : l'heure de destruction, et la durée totale d'existence de la copie. Fermer le tunnel SSH et vider `PGPASSWORD` du shell.

> La suppression d'un Flexible Server crée par défaut ses propres sauvegardes de rétention. Vérifier qu'aucun serveur supprimé restaurable ne subsiste : `az postgres flexible-server list-deleted-server -l westeurope --query "[].name" -o tsv`. Si la copie y figure, la durée de conservation de ces sauvegardes est à porter dans les constats — c'est une rémanence de données réelles.

---

## 5. Limite connue : les suppressions ne sont pas rejouables

La [politique de conservation](../gdpr/data-retention-policy.md#32-test-de-restauration) annonce une « réapplication des suppressions intervenues depuis ». **Le code ne le permet pas, et c'est un choix délibéré.**

L'entrée d'audit `AccountDeleted` est écrite sans aucune donnée identifiante — ni `UserId`, ni e-mail, ni identifiant de l'entité supprimée (`src/HouseFlow.Application/Services/UserAccountService.cs`, écriture de l'entrée finale). Elle atteste **qu'**une suppression a eu lieu et **quand**, jamais **laquelle**. C'est ce qui garantit qu'une suppression de compte ne laisse pas de trace rattachable — et cela interdit du même coup de rejouer les suppressions depuis le journal.

**Conséquences, à connaître avant d'en avoir besoin :**

1. **Tant que la base vivante existe**, les comptes à re-supprimer s'obtiennent par **différence** entre la copie restaurée et la production : les `Users.Id` présents dans la copie et absents en production sont exactement les comptes supprimés depuis `RESTORE_TIME`. C'est le seul chemin, et il suppose que la production soit encore lisible.
2. **Si la production est perdue** et qu'une restauration à un instant antérieur la remplace, les comptes supprimés entre cet instant et l'incident **ressuscitent**, et rien ne permet de savoir lesquels. Des données qui devaient être effacées redeviennent disponibles : c'est une **violation de données** au sens de l'Art. 4(12), à traiter par la [procédure de notification](./breach-notification-procedure.md) — pas un simple incident d'exploitation.
3. **La seule mesure d'atténuation est la proximité temporelle** : le PITR restaure à la seconde près dans la fenêtre de 7 jours. Restaurer au plus près de l'incident réduit la fenêtre de résurrection, souvent à zéro.

Cette limite est **documentée plutôt que corrigée** : la corriger supposerait de conserver l'identifiant des comptes supprimés, c'est-à-dire de garder une trace de la personne dont on vient d'effacer les données. Le compromis retenu privilégie l'effacement.

---

## 6. Écueils constatés

| Symptôme | Cause | Réponse |
|---|---|---|
| La restauration échoue faute d'adresses | `snet-db` est un /28 déjà occupé par la source | Étape 3 — subnet dédié `snet-db-restore` |
| `psql` refuse la connexion, mot de passe invalide | Le jeton Entra a expiré (une heure) | Régénérer `PGPASSWORD` |
| Le tunnel SSH échoue à la première tentative | `ca-bastion-prod` est *scale-to-zero* | Réessayer après le démarrage à froid |
| `RESTORE_TIME` refusé | Antérieur à `earliestRestoreDate`, ou serveur redémarré depuis | Relever à nouveau l'étape 1 et rapprocher l'instant |
| Restauration anormalement longue | SKU `B_Standard_B1ms`, 32 Go | Attendre ; au-delà de 45 minutes, le noter comme écart |

---

## 7. Fiche de preuve

À remplir **pendant** le test, pas après. Une fiche incomplète vaut un test non fait.

| Élément | Valeur relevée |
|---|---|
| Date et heure de début (UTC) | |
| Opérateur | |
| `earliestRestoreDate` de la source | |
| Rétention annoncée / constatée | 7 jours / |
| `RESTORE_TIME` demandé | |
| Nom du serveur restauré | |
| Lancement → disponibilité de la copie | |
| 6.1 — tables applicatives (attendu : 11) | |
| 6.2 — dernière migration (identique à la production ?) | |
| 6.3 — écarts de volumétrie constatés | |
| 6.4 — dernier événement audité dans la copie | |
| 6.5 — clés étrangères non validées (attendu : 0) | |
| Heure de destruction de la copie | |
| Durée totale d'existence de la copie (max 4 h) | |
| Serveurs supprimés restaurables subsistant ? | |
| **Verdict** : les quatre affirmations du § 1 sont-elles établies ? | |
| Écarts constatés | |
| Actions correctives, responsable, échéance | |
| Mises à jour apportées à la présente procédure | |

### Historique des tests

| Date | Opérateur | Instant restauré | Durée de restauration | Verdict | Écarts / actions |
|---|---|---|---|---|---|
| *(aucun test réalisé à ce jour)* | | | | | |

À l'issue du test, reporter la date dans la [politique de conservation § 3.2](../gdpr/data-retention-policy.md#32-test-de-restauration) et dans le tableau de la [revue annuelle](../gdpr/README.md#63-revue-annuelle).

---

## 8. Documents liés

- [Politique de conservation des données § 3](../gdpr/data-retention-policy.md#3-sauvegardes) — sauvegardes, rotation, et la présente exigence de test
- [Procédure de notification de violation](./breach-notification-procedure.md) — § 1 pour la qualification d'une résurrection de données effacées en violation, § 4 pour l'arbre de décision
- [Registre des activités de traitement, annexe C](../gdpr/processing-register.md) — mesures de sécurité de l'Art. 32
- [Index du dossier de conformité RGPD](../gdpr/README.md)
