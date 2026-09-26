# Politique de conservation des données

**Articles 5(1)(e) et 5(2) du RGPD — limitation de la conservation et accountability**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL** (service HouseFlow) |
| **Contact vie privée** | `privacy@houseflow.cloud` |
| **Date** | 2026-09-11 |
| **Version** | 1.0 |
| **Revue** | annuelle, et à chaque évolution du [registre des traitements](./processing-register.md) |

> **Principe directeur.** L'article 5(1)(e) impose que les données soient conservées « sous une forme permettant l'identification des personnes concernées pendant une durée n'excédant pas celle nécessaire au regard des finalités ». Lors d'un contrôle, l'autorité vérifie en premier lieu **la durée réellement appliquée en base** — typiquement par un export des enregistrements les plus anciens — et non celle qui est annoncée. Une durée écrite sans mécanisme de purge est systématiquement requalifiée en manquement. Chaque ligne du présent document est donc associée à un **mécanisme technique effectif**.

---

## 1. Les trois phases du cycle de vie

La CNIL structure le cycle de vie de la donnée en trois phases. Voici leur déclinaison pour HouseFlow.

### Phase 1 — Base active

Les données sont utilisées couramment par les services opérationnels et accessibles dans l'outil de travail.

Pour HouseFlow, la base active correspond à la **base PostgreSQL de production**, accessible à l'utilisateur via l'application tant que son compte existe. Elle couvre : le compte, les maisons, les équipements, les types d'entretien, les interventions, les adhésions, les invitations en cours, les sessions actives et les journaux d'audit de moins d'un an.

### Phase 2 — Archivage intermédiaire

Les données ne sont plus d'usage courant, mais présentent encore un intérêt administratif ou doivent être conservées pour satisfaire une obligation légale ou un délai de prescription. Elles font l'objet d'un **accès restreint**, limité aux personnes ayant un intérêt spécifique à les connaître.

Pour HouseFlow, cette phase est volontairement **réduite au strict minimum** :

| Donnée archivée | Justification | Accès restreint à |
|---|---|---|
| Journaux d'audit entre 1 et 3 ans | Sous forme **anonymisée** uniquement — ils sortent alors du champ du RGPD (considérant 26) et ne sont conservés que pour des statistiques de sécurité et de volumétrie. | Éditeur |
| [Journal des demandes d'exercice de droits](./rights-requests-log.md) (3 ans) | Preuve du respect des articles 12 à 22 (accountability, Art. 5(2)). Conservé hors base applicative, hors du dépôt Git, dans l'espace documentaire du responsable (le dépôt est public). | Référent vie privée |
| [Registre des violations](../security/breach-register.md) (5 ans) | Obligation de documentation de l'**Art. 33(5)**, sans seuil ni exception. | Référent vie privée |
| Sauvegardes PITR (7 jours glissants) | Continuité de service et restauration après incident (Art. 32(1)(c)). Jamais consultées à des fins d'exploitation courante. | Éditeur, via la plateforme Azure |

**Absence d'archivage comptable.** HouseFlow ne facture pas et ne traite aucune donnée de paiement. L'obligation de conservation de dix ans des pièces justificatives (art. L123-22 du code de commerce), qui fonde habituellement l'exception de l'**Art. 17(3)(b)**, **est sans objet à ce jour**. Elle deviendrait applicable dès l'introduction d'une offre payante ; le présent document et le registre devraient alors être mis à jour.

### Phase 3 — Suppression ou anonymisation

À l'issue de la durée d'archivage, les données sont **supprimées définitivement**, ou **anonymisées** de façon irréversible.

L'anonymisation n'est retenue que lorsqu'elle est **réelle** au sens de l'avis **WP216** du G29 : les trois critères d'individualisation, de corrélation et d'inférence doivent être simultanément neutralisés. Tronquer une adresse IP tout en conservant à côté un `UserId` et un `UserAgent` ne constitue **pas** une anonymisation — le lien de corrélation subsiste. C'est pourquoi l'anonymisation des journaux d'audit efface **l'ensemble** des champs identifiants (`UserId`, `Username`, `IpAddress`, `UserAgent`, `OldValues`, `NewValues`, `ChangedProperties`), et non la seule adresse IP.

---

## 2. Tableau des durées de conservation

**Ce tableau est la source unique.** Il est repris à l'identique dans le [registre des traitements](./processing-register.md) et dans la politique de confidentialité publiée sur `/privacy`. Toute divergence est un défaut à corriger.

| # | Donnée | Durée | Justification | Mécanisme technique |
|---|---|---|---|---|
| 1 | **Compte et données de maisons, appareils, entretiens** | Durée de vie du compte ; **suppression immédiate et définitive à la demande** (pas de période de grâce) | Base contractuelle (Art. 6(1)(b)) : les données sont nécessaires tant que le contrat produit ses effets. La suppression immédiate met en œuvre l'Art. 17(1)(a) sans délai supplémentaire. | `DELETE /api/v1/users/me` — suppression synchrone, propagée en cascade PostgreSQL aux `Devices`, `MaintenanceTypes`, `MaintenanceInstances`, `HouseMembers` et `Invitations`. Voir [§ 4](#4-suppression-de-compte). |
| 2 | **Comptes inactifs** | **3 ans** sans connexion → suppression après préavis | Au-delà de 3 ans sans connexion, les données ne sont plus nécessaires à la finalité poursuivie (Art. 5(1)(e)). La CNIL a jugé proportionnée une suppression après 2 ans d'inactivité, sous réserve d'un avertissement préalable ; la durée de 3 ans, plus favorable à l'utilisateur, reste dans la fourchette admise. | **Procédure manuelle documentée** tant que l'envoi d'emails n'est pas implémenté. Voir [§ 5](#5-comptes-inactifs). |
| 3 | **Refresh tokens révoqués ou expirés** | Purgés **30 jours** après révocation ou expiration | Un jeton révoqué n'a plus d'usage fonctionnel. Les 30 jours couvrent l'investigation d'un incident signalé tardivement et la détection d'une tentative de réutilisation d'un jeton révoqué. | `DataRetentionJob` — `RevokedRefreshTokenRetentionDays` |
| 4 | **Clés API révoquées** | Purgées **30 jours** après révocation | Même raisonnement que pour les refresh tokens. Seul le hachage SHA-256 est stocké : la clé en clair n'existe nulle part. | `DataRetentionJob` — `RevokedApiKeyRetentionDays` |
| 5 | **Adresses IP** (journaux d'audit, refresh tokens, clés API) | **Complètes 30 jours**, puis **tronquées** (anonymisation partielle) | L'IP est une donnée personnelle (CJUE, *Breyer*, C-582/14). Elle n'est nécessaire sous forme complète que pour l'investigation « à chaud ». Au-delà, la troncature suffit à conserver une information de contexte tout en supprimant la précision permettant une géolocalisation fine. | `DataRetentionJob` — `IpAnonymizeAfterDays`, via `IpAddressAnonymizer.Anonymize()` (IPv4 : dernier octet à 0 ; IPv6 : 80 derniers bits à 0) |
| 6 | **Journaux d'audit** | **1 an** sous forme identifiante, puis **anonymisés** ; **purge définitive à 3 ans** | 1 an : durée retenue dans la fourchette de 6 mois à 1 an de la recommandation CNIL relative aux mesures de journalisation, au titre de l'intérêt légitime de sécurité — voir l'[arbitrage sur le décret 2021-1362](#61--décret-n-2021-1362--non-retenu). 3 ans : limite haute admise par la CNIL pour des dispositifs de contrôle interne justifiés, appliquée ici à des données déjà anonymisées. | `DataRetentionJob` — `AuditLogAnonymizeAfterDays` puis `AuditLogDeleteAfterDays` |
| 7 | **Invitations non acceptées, expirées ou révoquées** | **30 jours** après expiration | Une invitation expirée ne peut plus être acceptée. Les 30 jours permettent de tracer un partage contesté. | `DataRetentionJob` — `ExpiredInvitationRetentionDays` : marque `Expired` les invitations `Pending` échues, puis supprime les invitations non `Pending` dont `ExpiresAt` remonte à plus de 30 jours (reprend l'ancien `CleanupExpiredInvitationsJob`, fusionné). |
| 8 | **Journal des demandes d'exercice de droits** | **3 ans** | Preuve du respect des articles 12 à 22 (accountability, Art. 5(2)) sur une durée couvrant une éventuelle réclamation ou un contrôle. | Tenue manuelle dans [`rights-requests-log.md`](./rights-requests-log.md) ; purge à la revue annuelle. |
| 9 | **Sauvegardes Azure PostgreSQL** | **Rotation 7 jours** (PITR) | Valeur réelle configurée : `backup_retention_days = 7` dans `infrastructure/terraform/environment/postgresql.tf` — valeur par défaut d'Azure Database for PostgreSQL Flexible Server. | Géré par la plateforme Azure. Voir [§ 3](#3-sauvegardes). |

### 2.1 Paramétrage applicatif

Les durées automatisées sont portées par la section `DataRetention` de `src/HouseFlow.API/appsettings.json` :

```jsonc
{
  "DataRetention": {
    "IpAnonymizeAfterDays": 30,              // ligne 5 — troncature des IP (audit, refresh tokens, clés API)
    "RevokedRefreshTokenRetentionDays": 30,  // ligne 3 — purge des jetons révoqués/expirés
    "RevokedApiKeyRetentionDays": 30,        // ligne 4 — purge des clés API révoquées
    "AuditLogAnonymizeAfterDays": 365,       // ligne 6 — anonymisation des journaux d'audit
    "AuditLogDeleteAfterDays": 1095,         // ligne 6 — purge définitive des journaux d'audit
    "SoftDeletedRetentionDays": 30,          // purge définitive des entités ISoftDeletable (aucune entité concrète à ce jour)
    "ExpiredInvitationRetentionDays": 30,    // ligne 7 — invitations expirées/révoquées
    "BatchSize": 500,                        // taille des lots (évite les verrous longs)
    "Cron": "0 3 * * *"                      // exécution quotidienne à 03:00 UTC
  }
}
```

> Le seuil des **comptes inactifs** (ligne 2, 3 ans) n'est pas un paramètre du job : la procédure est manuelle (§ 5). Il n'existe pas d'interrupteur de désactivation du job : en environnement de test, les tests d'intégration exécutent le job directement avec un `TimeProvider` contrôlé.

> **Règle de cohérence.** Ces valeurs et le tableau ci-dessus doivent rester strictement alignés. Toute modification de l'une impose la modification de l'autre, ainsi que celle de la politique de confidentialité publiée, dans le même changement.

### 2.2 Le job de purge

| Élément | Valeur |
|---|---|
| **Implémentation** | `src/HouseFlow.Infrastructure/Jobs/DataRetentionJob.cs` |
| **Ordonnanceur** | Hangfire, tâche récurrente |
| **Fréquence** | **Quotidienne, à 03:00 UTC** — heure creuse, hors des pics d'utilisation |
| **Idempotence** | Chaque passe est bornée par une date de coupure calculée à l'exécution ; une exécution répétée ne produit aucun effet supplémentaire. |
| **Journalisation** | Chaque exécution consigne les volumes traités par catégorie (IP tronquées, jetons supprimés, clés supprimées, entrées anonymisées, entrées purgées). **Ce journal est la preuve d'accountability que l'autorité demandera** : il démontre que la durée annoncée est effectivement appliquée. |
| **Résilience** | Chaque règle s'exécute dans son propre bloc d'erreur : l'échec d'une règle n'empêche pas les autres. Les mises à jour se font par `ExecuteUpdate`/`ExecuteDelete` (aucune entrée d'audit générée par la purge elle-même). |

**Ordre d'exécution des règles** (tel qu'implémenté dans `DataRetentionJob.BuildRules` — règles indépendantes, chacune dans son propre bloc d'erreur) :

1. **Troncature des IP** de plus de 30 jours (`AuditLogs.IpAddress`, `RefreshTokens.CreatedByIp` et `RevokedByIp`, `ApiKeys.CreatedByIp`).
2. **Anonymisation** des entrées d'audit de plus d'un an : `UserId` → `null`, `Username` → `null`, `IpAddress` → `null`, `UserAgent` → `null`, `OldValues` / `NewValues` / `ChangedProperties` / `AdditionalData` → `null`. `EntityType`, `EntityId`, `Action` et `Timestamp` sont conservés à des fins statistiques.
3. **Suppression définitive** des entrées d'audit de plus de trois ans.
4. **Suppression** des refresh tokens révoqués ou expirés depuis plus de 30 jours.
5. **Suppression** des clés API révoquées depuis plus de 30 jours.
6. **Purge** des entités soft-deleted (`ISoftDeletable`) depuis plus de 30 jours.
7. **Invitations** : marquage `Expired` des invitations `Pending` échues, puis suppression des invitations non `Pending` (acceptées comprises) expirées depuis plus de 30 jours.

Le job est annoté `[DisableConcurrentExecution]` : une seule passe à la fois, même avec plusieurs réplicas.

---

## 3. Sauvegardes

| Élément | Valeur réelle |
|---|---|
| **Mécanisme** | Azure Database for PostgreSQL Flexible Server — sauvegarde automatique avec **restauration dans le temps (PITR)** |
| **Rétention** | **7 jours** (`backup_retention_days = 7`) |
| **Redondance géographique** | **Désactivée** (`geo_redundant_backup_enabled = false`) — les sauvegardes demeurent dans la région **West Europe**, donc dans l'EEE. Aucun transfert hors EEE par ce canal. |
| **Chiffrement** | Chiffrement au repos assuré par la plateforme Azure. |
| **Configuration** | `infrastructure/terraform/environment/postgresql.tf` |

### 3.1 Articulation avec le droit à l'effacement

Une suppression demandée au titre de l'article 17 est **immédiate en base active**. Elle ne peut en revanche pas être propagée dans les sauvegardes déjà constituées : celles-ci sont immuables par construction.

Position retenue, conforme à la doctrine de la CNIL :

1. Les sauvegardes ont un **cycle de rotation défini et documenté** : **7 jours**. Passé ce délai, aucune trace de la donnée supprimée ne subsiste dans le dispositif de sauvegarde.
2. Les sauvegardes ne sont **jamais consultées ni exploitées** à des fins de traitement courant : leur seule finalité est la restauration après incident.
3. En cas de **restauration effective**, les suppressions intervenues entre la date de la sauvegarde et la date de restauration doivent être **réappliquées** — étape obligatoire de la procédure de restauration, à exécuter avant toute remise en service.
4. L'utilisateur est **informé de ce délai de 7 jours** dans la politique de confidentialité, au titre de l'Art. 13(2)(a).

### 3.2 Test de restauration

L'article **32(1)(c)** impose de disposer de moyens permettant de rétablir la disponibilité des données, et l'article **32(1)(d)** d'en tester régulièrement l'efficacité. Un test de restauration ne peut donc se contenter d'exister sur le papier.

| Élément | État |
|---|---|
| Périodicité retenue | **Annuelle**, et après tout changement de version majeure de PostgreSQL, de SKU ou de topologie réseau |
| Procédure | **[Test de restauration de sauvegarde — mode opératoire](../security/backup-restore-drill.md)** : relevé de la fenêtre réelle, restauration PITR sur un serveur temporaire dans un subnet dédié, vérification du schéma, des migrations, de la volumétrie et de l'intégrité référentielle, puis destruction vérifiée de la copie. Le mode opératoire porte la fiche de preuve et l'historique des tests. |
| Sort des suppressions intervenues depuis l'instant restauré | **Non rejouables depuis le journal d'audit** — l'entrée `AccountDeleted` est écrite sans identifiant, par construction. Elles ne s'obtiennent que par différence avec la base vivante ; si celle-ci est perdue, la résurrection des comptes supprimés constitue une violation de données. Limite documentée au [§ 5 du mode opératoire](../security/backup-restore-drill.md#5-limite-connue--les-suppressions-ne-sont-pas-rejouables). |
| Dernier test réalisé | *[à compléter — reporter la date depuis la fiche de preuve]* |
| Prochain test prévu | *[à compléter]* |

---

## 4. Suppression de compte

**Aucune période de grâce** (décision documentée du 2026-09-11, voir [§ 6.3](#63--absence-de-période-de-grâce)). La suppression est **immédiate et définitive** dès la confirmation par l'utilisateur.

Opérations exécutées par `DELETE /api/v1/users/me` :

| Donnée | Traitement appliqué |
|---|---|
| **Compte** (`Users`) | Supprimé — email, prénom, nom, hachage de mot de passe, préférences. |
| **Maisons dont l'utilisateur est propriétaire** | Si un autre membre **non-locataire** existe (`CollaboratorRW` prioritaire, puis `CollaboratorRO`) : la propriété lui est **transférée** (`House.UserId` mis à jour, rôle porté à `Owner`) — les données des co-utilisateurs sont ainsi préservées. **Sinon** : la maison est **supprimée** avec l'intégralité de son contenu (équipements, types d'entretien, interventions, adhésions, invitations). |
| **Adhésions aux maisons d'autrui** (`HouseMembers`) | Supprimées — l'utilisateur est retiré de toutes les maisons auxquelles il participait. |
| **Refresh tokens** | Supprimés — **toutes les sessions sont révoquées**. |
| **Clés API** | Supprimées. |
| **Invitations créées par l'utilisateur** | Supprimées. |
| **Journaux d'audit le concernant** | **Anonymisés immédiatement**, sans attendre l'échéance d'un an : `UserId` → `null`, `Username` → `deleted-user` (`GdprPolicy.DeletedUserName`), `IpAddress`, `UserAgent`, `OldValues`, `NewValues`, `ChangedProperties` → `null`. |
| **Trace de la suppression** | Une entrée d'audit `Action = "AccountDeleted"` est écrite, **sans aucune donnée identifiante**. |

### 4.1 Limite documentée — le JWT d'accès

Le jeton d'accès JWT est **par construction non révocable** : sa validité est vérifiée par signature, sans consultation de la base. Un JWT émis avant la suppression reste donc techniquement valide jusqu'à son expiration, soit **au maximum 15 minutes**.

Cette limite est **sans conséquence pratique** :

- toutes les données de l'utilisateur ont déjà été supprimées ou transférées — le jeton ne donne accès à rien ;
- le rafraîchissement échoue immédiatement, le refresh token ayant été supprimé ;
- la fenêtre résiduelle est de 15 minutes au plus.

Ce point est documenté dans la politique de confidentialité. En cas d'incident de sécurité, la rotation de `JWT__KEY` invalide instantanément **tous** les JWT en circulation — voir la [procédure de notification de violation](../security/breach-notification-procedure.md).

### 4.2 Transfert des maisons partagées

Le devenir d'une maison partagée à la suppression du compte de son propriétaire relève d'un arbitrage documenté — voir [§ 6.4](#64--maisons-partagées--transfert-au-collaborateur-le-plus-ancien).

---

## 5. Comptes inactifs

**Durée retenue : 3 ans sans connexion.** L'envoi d'emails n'étant pas encore implémenté, la procédure est **manuelle** et exécutée à la revue annuelle par le référent vie privée.

### 5.1 Étape 1 — Identification

La colonne `Users.LastLoginAt` (horodatage UTC de la dernière connexion par mot de passe, indexée) est renseignée par `AuthService.LoginAsync`. Elle est indépendante de l'anonymisation des journaux et constitue la référence. Pour les comptes créés avant son introduction (2026-09-12) et jamais reconnectés depuis, elle vaut `NULL` : on retombe alors sur la date de création du compte (choix conservateur).

```sql
-- Comptes sans connexion depuis plus de 3 ans (1095 jours).
SELECT u."Id", u."Email", u."CreatedAt", COALESCE(u."LastLoginAt", u."CreatedAt") AS "LastActivity"
FROM "Users" u
WHERE COALESCE(u."LastLoginAt", u."CreatedAt") < (NOW() AT TIME ZONE 'UTC') - INTERVAL '1095 days'
  AND u."ProcessingRestrictedAt" IS NULL   -- un compte sous limitation (Art. 18) n'est jamais purgé
ORDER BY "LastActivity";
```

### 5.2 Étape 2 — Préavis

Un **email de préavis** est adressé à chaque compte identifié, **30 jours** avant la suppression. Il indique la date de suppression prévue, précise qu'une simple connexion suffit à conserver le compte, et rappelle la possibilité d'exporter ses données au préalable.

> **État actuel : fonctionnalité d'envoi d'emails non implémentée.** Tant qu'elle n'existe pas, le préavis est envoyé **manuellement** depuis la messagerie de l'éditeur, à partir de la liste issue de l'étape 1. **Aucun compte ne peut être supprimé sans préavis préalable** : cette exigence est impérative.

### 5.3 Étape 3 — Suppression

À l'expiration du préavis, et pour les seuls comptes n'ayant enregistré aucune connexion entre-temps, la suppression est exécutée **selon exactement la même logique que le droit à l'effacement** décrit au [§ 4](#4-suppression-de-compte) : transfert ou suppression des maisons, retrait des adhésions, suppression des jetons et clés, anonymisation des journaux, écriture d'une entrée `AccountDeleted`.

Chaque campagne est consignée : date, nombre de comptes identifiés, nombre de préavis envoyés, nombre de comptes réactivés, nombre de comptes supprimés.

| Date de campagne | Comptes identifiés | Préavis envoyés | Réactivés | Supprimés | Opérateur |
|---|---|---|---|---|---|
| *(aucune campagne à ce jour)* | | | | | |

### 5.4 Amélioration prévue

L'implémentation de l'envoi d'emails transactionnels permettra d'automatiser les étapes 2 et 3 dans `DataRetentionJob` (nouveau paramètre `InactiveAccountDays` à introduire à ce moment-là), sur la base de `Users.LastLoginAt`.

---

## 6. Arbitrages documentés

L'article 5(2) impose de pouvoir **démontrer** le respect des principes. Les quatre décisions suivantes sont consignées par écrit et datées, afin d'être produites lors d'un contrôle.

### 6.1 — Décret n° 2021-1362 : non retenu

**Question.** Le décret du 20 octobre 2021, pris pour l'application du II de l'article 6 de la LCEN, impose aux intermédiaires techniques de conserver pendant **un an** les données permettant d'identifier toute personne ayant contribué à la création d'un contenu mis en ligne. S'applique-t-il à HouseFlow ?

**Analyse.** Ce texte vise les **hébergeurs de contenus destinés au public**. HouseFlow est un service privé de gestion de maintenance immobilière : les données saisies (maisons, équipements, interventions) ne sont accessibles qu'aux membres de la maison concernée et ne sont, à aucun moment, mises à la disposition du public. Le service ne publie aucun contenu, n'héberge aucun espace d'expression publique et ne comporte aucune fonctionnalité de diffusion.

**Décision (2026-09-11).** **Ne pas se prévaloir du décret 2021-1362** comme fondement d'une obligation légale de conservation. La durée d'**un an** appliquée aux journaux d'audit est retenue au titre de l'**intérêt légitime de sécurité** (Art. 6(1)(f)), dans la fourchette de 6 mois à 1 an recommandée par la CNIL dans sa recommandation relative aux mesures de journalisation. Elle est justifiée par le [LIA](./legitimate-interest-assessment.md#1--journaux-daudit) et ouvre par conséquent un droit d'opposition au titre de l'Art. 21(1), traité par une réponse motivée.

**Conséquence pratique.** Invoquer une obligation légale inexistante aurait été doublement fautif : cela aurait privé les personnes de leur droit d'opposition et exposé le responsable à un grief de base légale erronée.

### 6.2 — Autorité de contrôle chef de file : APD (Belgique)

**Question.** Quelle autorité est compétente comme chef de file au sens de l'article 56 ?

**Analyse.** Le guichet unique désigne l'autorité de l'**établissement principal** du responsable de traitement. La base d'utilisateurs est franco-belge et l'hébergement se situe aux Pays-Bas, mais ces éléments sont **sans incidence** sur la détermination de l'autorité chef de file : seul compte le lieu d'établissement principal de l'éditeur.

**Décision (2026-09-23).** Le responsable de traitement est **Rouss Consulting SRL**, société de droit belge : l'établissement principal est en **Belgique**, et l'autorité chef de file est donc l'**Autorité de protection des données**.

- **APD / GBA** *(chef de file)* — Rue de la Presse 35, 1000 Bruxelles — `contact@apd-gba.be` — [autoriteprotectiondonnees.be](https://www.autoriteprotectiondonnees.be)
- **CNIL** *(compétente pour les personnes résidant en France, au titre de l'Art. 77(1))* — 3 Place de Fontenoy, TSA 80715, 75334 Paris Cedex 07 — [cnil.fr/fr/plaintes](https://www.cnil.fr/fr/plaintes)

Il est en outre rappelé, conformément à l'**Art. 77(1)**, que toute personne peut saisir l'autorité de **son propre État membre** de résidence habituelle ou de travail, indépendamment du lieu d'établissement de l'éditeur.

**Conséquence.** Le **canal de notification** en cas de violation est celui de l'**APD** — voir la [procédure de notification de violation](../security/breach-notification-procedure.md), qui doit être suivie dans les 72 heures.

### 6.3 — Absence de période de grâce

**Question.** Faut-il instaurer une période de grâce entre la demande de suppression de compte et la purge définitive ?

**Analyse.** Une période de grâce de 30 jours maximum est licite et protège contre la suppression accidentelle et contre la suppression malveillante consécutive à une compromission de compte. Elle a toutefois un coût pour la personne : ses données demeurent conservées après qu'elle en a demandé l'effacement. Elle impose en outre un état intermédiaire (compte désactivé mais non purgé) qui doit être géré, annoncé, et assorti d'un email d'annulation — fonctionnalité d'emailing qui **n'existe pas encore**. Une période de grâce sans mécanisme d'annulation cumulerait les inconvénients des deux options, sans en offrir les avantages.

**Décision (2026-09-11).** **Suppression immédiate et définitive, sans période de grâce.** Cette option est la plus protectrice au regard de l'article 17(1) et la plus lisible pour l'utilisateur. Les garde-fous sont d'une autre nature : la suppression est confirmée explicitement dans l'interface et l'utilisateur est invité à **exporter ses données au préalable** (`GET /api/v1/users/me/export`).

**Conséquence.** La politique de confidentialité annonce clairement que la suppression est immédiate et irréversible, à la seule réserve du délai de rotation des sauvegardes (7 jours) et de la fenêtre résiduelle de 15 minutes du JWT d'accès.

### 6.4 — Maisons partagées : transfert au collaborateur le plus ancien

**Question.** Que devient une maison partagée lorsque son propriétaire supprime son compte ?

**Analyse.** Une maison, ses équipements et son historique d'entretien constituent des données **partagées** : elles forment aussi l'historique des autres membres, qui les ont parfois alimentées. Les détruire reviendrait à effacer les données de tiers qui n'ont rien demandé — au-delà du droit du partant, qui porte sur **ses** données. À l'inverse, conserver une maison sans propriétaire produirait un enregistrement orphelin que plus personne ne pourrait administrer ni supprimer, en contradiction avec l'article 5(1)(e).

**Décision (2026-09-11).** À la suppression du compte propriétaire :

1. **S'il existe au moins un autre membre non-locataire**, la propriété est **transférée** au plus ancien d'entre eux, `CollaboratorRW` prioritaire sur `CollaboratorRO` (`House.UserId` mis à jour, rôle porté à `Owner`). Le critère d'ancienneté (`HouseMembers.CreatedAt`) est objectif, déterministe et vérifiable.
2. **S'il n'existe aucun autre membre non-locataire**, la maison est **supprimée** avec l'intégralité de son contenu.

Les **locataires** (`Tenant`) sont volontairement exclus de la dévolution : leur rôle traduit un accès en lecture restreinte à un logement qu'ils occupent sans l'administrer ; leur transférer la propriété du bien et de son historique excéderait manifestement leurs droits.

Les **références nominatives** à l'utilisateur supprimé sont anonymisées dans les journaux d'audit conservés au titre de la maison transférée.

**Information.** Ce mécanisme est décrit dans la politique de confidentialité et rappelé dans l'écran de confirmation de suppression de compte, afin que l'utilisateur sache ce qu'il advient des maisons qu'il a créées.

---

## 7. Journaux applicatifs

À distinguer strictement des **journaux d'audit** (table `AuditLogs`, ligne 6 du tableau), qui sont des données applicatives soumises au job de purge.

| Élément | Valeur |
|---|---|
| **Bibliothèque** | Serilog, niveau minimal `Information`, sortie console asynchrone |
| **Collecte** | Sortie standard du conteneur → Azure Container Apps → Log Analytics |
| **Rétention** | **30 jours** — `retention_in_days = 30` dans `infrastructure/terraform/environment/container-env.tf`. Cette valeur coïncide avec la période gratuite par défaut d'Azure Monitor ; elle est ici **explicitement configurée**, et non subie. |
| **Contenu personnel** | **Aucune donnée personnelle depuis le 2026-09-11.** Les événements d'authentification sont journalisés par identifiant technique (`UserId`), jamais par adresse email. Aucun mot de passe, jeton ou secret n'y figure. |
| **Accès** | Restreint aux administrateurs Azure, sous authentification Entra ID. |

**Point de vigilance permanent.** Toute nouvelle instruction de journalisation doit être vérifiée avant fusion : **aucune adresse email, aucun nom, aucun secret ne doit être écrit dans les journaux applicatifs**. Ces journaux échappent au `DataRetentionJob` et ne sont ni anonymisés à la suppression de compte, ni couverts par l'export de données.

---

## 8. Contrôle de conformité

Vérifications à exécuter **annuellement**, et à consigner. Elles reproduisent ce qu'une autorité de contrôle demanderait en priorité : la preuve que la durée annoncée est la durée réellement appliquée.

```sql
-- 1. Entrée d'audit la plus ancienne : doit avoir moins de 3 ans (1095 jours)
SELECT MIN("Timestamp") AS oldest_audit FROM "AuditLogs";

-- 2. Aucune entrée d'audit de plus d'un an ne doit rester identifiante
SELECT count(*) AS non_anonymized_over_1y
FROM "AuditLogs"
WHERE "Timestamp" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '365 days'
  AND ("UserId" IS NOT NULL OR "Username" IS NOT NULL OR "IpAddress" IS NOT NULL);
-- Attendu : 0

-- 3. Aucun refresh token révoqué ou expiré de plus de 30 jours ne doit subsister
SELECT count(*) AS stale_tokens
FROM "RefreshTokens"
WHERE ("RevokedAt" IS NOT NULL AND "RevokedAt" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 days')
   OR ("ExpiresAt" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 days');
-- Attendu : 0

-- 4. Aucune clé API révoquée de plus de 30 jours ne doit subsister
SELECT count(*) AS stale_api_keys
FROM "ApiKeys"
WHERE "RevokedAt" IS NOT NULL
  AND "RevokedAt" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 days';
-- Attendu : 0

-- 5. Aucune IP complète de plus de 30 jours ne doit subsister dans les journaux d'audit
--    (une IPv4 tronquée se termine par « .0 »)
SELECT count(*) AS untruncated_ips
FROM "AuditLogs"
WHERE "Timestamp" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 days'
  AND "IpAddress" IS NOT NULL
  AND "IpAddress" NOT LIKE '%.0'
  AND "IpAddress" NOT LIKE '%::'
  AND "IpAddress" <> 'anonymized';
-- Attendu : 0

-- 6. Aucune invitation non « Pending » expirée depuis plus de 30 jours ne doit subsister
SELECT count(*) AS stale_invitations
FROM "Invitations"
WHERE "Status" <> 'Pending'
  AND "ExpiresAt" < (NOW() AT TIME ZONE 'UTC') - INTERVAL '30 days';
-- Attendu : 0
```

| Date du contrôle | Opérateur | Résultat | Anomalies et suites données |
|---|---|---|---|
| *(aucun contrôle à ce jour)* | | | |

---

## 9. Documents liés

- [Index du dossier de conformité](./README.md)
- [Registre des activités de traitement](./processing-register.md)
- [Test de mise en balance des intérêts légitimes (LIA)](./legitimate-interest-assessment.md)
- [Sous-traitants et destinataires](./subprocessors.md)
- [Journal des demandes d'exercice de droits](./rights-requests-log.md)
- [Procédure de notification de violation de données](../security/breach-notification-procedure.md)
